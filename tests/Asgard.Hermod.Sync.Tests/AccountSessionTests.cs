using System.Text.Json.Nodes;
using Asgard.Heimdall.Client;

namespace Asgard.Hermod.Sync.Tests;

/// <summary>用官方 SDK 与真实签名协议验证登录、恢复、轮换、退出和失败保护。</summary>
public sealed class AccountSessionTests
{
    /// <summary>PKCE 登录完成后可重建服务恢复，普通退出撤销 RT 和 AT。</summary>
    [Fact]
    public async Task Login_RestoresUsernameAndRevokesOnlyApplicationCredentials()
    {
        using var protocol = new OAuthTestProtocol(); using var http = new HttpClient(protocol);
        using var oauth = new HeimdallOAuthClient(protocol.Options.ToOAuthOptions(), httpClient: http);
        var store = new MemoryAccountSessionStore();
        using (var first = new HeimdallAccountService(oauth, protocol.Options, protocol, store))
        {
            await first.SignInAsync(TestContext.Current.CancellationToken);
            Assert.True(protocol.VerifiedPkce); Assert.Equal("JWT-username", first.Account!.Username);
        }
        using var restored = new HeimdallAccountService(oauth, protocol.Options, protocol, store);
        await restored.RestoreAsync(TestContext.Current.CancellationToken);
        Assert.Equal("JWT-username", restored.Account!.Username);
        await restored.SignOutAsync(TestContext.Current.CancellationToken);
        Assert.Null(store.Session); Assert.Null(restored.Account); Assert.Equal(2, protocol.Revocations);
    }
    /// <summary>账号选择取消不会清除已有有效会话。</summary>
    [Fact]
    public async Task CancelledLogin_PreservesExistingSession()
    {
        using var protocol = new OAuthTestProtocol(); using var http = new HttpClient(protocol);
        using var oauth = new HeimdallOAuthClient(protocol.Options.ToOAuthOptions(), httpClient: http);
        var store = new MemoryAccountSessionStore(); using var account = new HeimdallAccountService(oauth, protocol.Options, protocol, store);
        await account.SignInAsync(TestContext.Current.CancellationToken); string? saved = store.Session;
        protocol.CancelLogin = true;
        await Assert.ThrowsAsync<OperationCanceledException>(() => account.SignInAsync(TestContext.Current.CancellationToken));
        Assert.Equal(saved, store.Session); Assert.Equal("user-a", account.Account!.SubjectId);
    }
    /// <summary>到期恢复使用原 sub/sid 约束，刷新不能切换到发行者最近选择的其他用户。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_RefreshesAndRejectsIdentityChange(bool changeSubject)
    {
        using var protocol = new OAuthTestProtocol(); using var http = new HttpClient(protocol);
        using var oauth = new HeimdallOAuthClient(protocol.Options.ToOAuthOptions(), httpClient: http);
        var store = new MemoryAccountSessionStore();
        using (var first = new HeimdallAccountService(oauth, protocol.Options, protocol, store)) await first.SignInAsync(TestContext.Current.CancellationToken);
        var saved = JsonNode.Parse(store.Session!)!; saved["ExpiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-10); store.Session = saved.ToJsonString();
        protocol.ChangeSubject = changeSubject;
        using var restored = new HeimdallAccountService(oauth, protocol.Options, protocol, store);
        if (changeSubject)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => restored.RestoreAsync(TestContext.Current.CancellationToken));
            Assert.Equal("SignInAgain", error.Data["Hermod.ResourceKey"]); Assert.Null(restored.Account); Assert.Null(store.Session);
        }
        else { await restored.RestoreAsync(TestContext.Current.CancellationToken); Assert.Equal("user-a", restored.Account!.SubjectId); }
        Assert.Equal(1, protocol.Refreshes);
    }
    /// <summary>数据操作持有原会话，退出等待操作结束，不能中途改变归属。</summary>
    [Fact]
    public async Task AuthenticatedOperation_BlocksSessionReplacementUntilCompletion()
    {
        using var protocol = new OAuthTestProtocol(); using var http = new HttpClient(protocol);
        using var oauth = new HeimdallOAuthClient(protocol.Options.ToOAuthOptions(), httpClient: http);
        var store = new MemoryAccountSessionStore(); using var account = new HeimdallAccountService(oauth, protocol.Options, protocol, store);
        await account.SignInAsync(TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = account.RunAuthenticatedAsync(async (identity, _) => { Assert.Equal("user-a", identity.SubjectId); entered.SetResult(); await finish.Task; }, TestContext.Current.CancellationToken);
        await entered.Task; var signOut = account.SignOutAsync(TestContext.Current.CancellationToken);
        Assert.False(signOut.IsCompleted); finish.SetResult(); await operation; await signOut; Assert.Null(account.Account);
    }
}
