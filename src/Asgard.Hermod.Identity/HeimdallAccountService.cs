namespace Asgard.Hermod.Identity;

/// <summary>串行提交登录、刷新与退出，避免迟到结果覆盖较新会话。</summary>
public sealed class HeimdallAccountService(HeimdallOAuthClient oauth, HermodIdentityOptions options,
    ILoginBrowser browser, IAccountSessionStore store, TimeProvider? timeProvider = null) : IAccountService, IDisposable
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SavedAccountSession? _session;
    /// <inheritdoc />
    public HermodAccount? Account { get; private set; }
    /// <inheritdoc />
    public event EventHandler? AccountChanged;

    /// <inheritdoc />
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (store.LoadSession() is not { } saved) return;
            var session = JsonSerializer.Deserialize<SavedAccountSession>(saved);
            if (session is null || session.Authority != options.Authority || session.ClientId != options.ClientId) return;
            HeimdallTokenSet tokens = session.Tokens;
            // 先读取发行者元数据，让 HTTPS 响应在到期判断前完成时钟校准。
            await oauth.GetDiscoveryAsync(cancellationToken);
            if (session.ExpiresAt <= _clock.GetUtcNow().AddSeconds(30))
            {
                if (tokens.RefreshToken is null) { store.SaveSession(null); return; }
                tokens = await oauth.RefreshTokenAsync(tokens.RefreshToken, session.SubjectId, session.SessionId, cancellationToken);
            }
            await CommitAsync(tokens, session.SubjectId, session.SessionId, cancellationToken);
        }
        catch (HeimdallClientException error) when (IsInvalidCredential(error))
        {
            store.SaveSession(null);
            throw Text.Exception("SignInAgain");
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var request = await oauth.CreateLoginRequestAsync(prompt: "select_account", cancellationToken: cancellationToken);
            await browser.AuthenticateAsync(request, async (callback, token) =>
            {
                var tokens = await oauth.ExchangeCodeAsync(callback, request, token);
                await CommitAsync(tokens, null, null, token);
            }, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await GetTokenLockedAsync(cancellationToken);
        }
        catch (HeimdallClientException error) when (IsInvalidCredential(error))
        {
            Clear();
            throw Text.Exception("SignInAgain");
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task RunAuthenticatedAsync(Func<HermodAccount, string, Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string token = await GetTokenLockedAsync(cancellationToken);
            await action(Account!, token);
        }
        catch (HeimdallClientException error) when (IsInvalidCredential(error))
        {
            Clear();
            throw Text.Exception("SignInAgain");
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task RunWithAccountAsync(Func<HermodAccount?, Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _gate.WaitAsync(cancellationToken);
        try { await action(Account); }
        finally { _gate.Release(); }
    }
    /// <summary>调用方已经持有会话锁，刷新结果只能更新同一身份。</summary>
    private async Task<string> GetTokenLockedAsync(CancellationToken cancellationToken)
    {
        var session = _session ?? throw Text.Exception("SignInAgain");
        if (session.ExpiresAt <= _clock.GetUtcNow().AddSeconds(30))
        {
            if (session.Tokens.RefreshToken is null) { Clear(); throw Text.Exception("SignInAgain"); }
            var tokens = await oauth.RefreshTokenAsync(session.Tokens.RefreshToken, session.SubjectId, session.SessionId, cancellationToken);
            await CommitAsync(tokens, session.SubjectId, session.SessionId, cancellationToken);
        }
        return _session!.Tokens.AccessToken;
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var previous = _session;
            Clear(); // 离线时本机仍能退出；撤销失败明确反馈，不能伪称已撤销。
            if (previous is null) return;
            if (previous.Tokens.RefreshToken is { } refresh) await oauth.RevokeAsync(refresh, "refresh_token", cancellationToken);
            await oauth.RevokeAsync(previous.Tokens.AccessToken, "access_token", cancellationToken);
        }
        finally { _gate.Release(); }
    }

    /// <summary>只从校验通过的 JWT 读取身份，缺少用户名时拒绝使用 ID 或邮箱替代。</summary>
    private async Task CommitAsync(HeimdallTokenSet tokens, string? subject, string? sid, CancellationToken cancellationToken)
    {
        var claims = await oauth.ValidateJwtAsync(tokens.AccessToken,
            new() { TokenType = "UserLogin", SubjectId = subject, SessionId = sid }, cancellationToken);
        var account = ReadAccount(claims);
        string? sessionId = claims.TryGetProperty("sid", out var value) ? value.GetString() : null;
        DateTimeOffset jwtExpiry = DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64());
        DateTimeOffset protocolExpiry = tokens.ObtainedAt.AddSeconds(tokens.ExpiresIn);
        var candidate = new SavedAccountSession(options.Authority, options.ClientId, account.SubjectId, sessionId, tokens,
            jwtExpiry < protocolExpiry ? jwtExpiry : protocolExpiry);
        cancellationToken.ThrowIfCancellationRequested();
        store.SaveSession(JsonSerializer.Serialize(candidate));
        _session = candidate;
        Account = account;
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Heimdall 租户用户的 name 是用户名；这里只处理已验签的载荷。</summary>
    public static HermodAccount ReadAccount(JsonElement validatedClaims)
    {
        string Read(string key) => validatedClaims.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw Text.Exception("LoginIdentityIncomplete");
        return new(Read("sub"), Read("tenant_id"), Read("application_id"), Read("name"));
    }

    /// <summary>网络失败保留存储会话，明确无效凭据才清除。</summary>
    private static bool IsInvalidCredential(HeimdallClientException error) => error.Category is
        "invalid_grant" or "invalid_jwt" or "identity_changed" or "token_boundary_mismatch";
    /// <summary>先持久化清除，再发布退出状态。</summary>
    private void Clear()
    {
        store.SaveSession(null);
        _session = null;
        Account = null;
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
