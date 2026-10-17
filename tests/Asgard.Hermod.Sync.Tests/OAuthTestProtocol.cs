using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Asgard.Heimdall.Client;
using Microsoft.IdentityModel.Tokens;

namespace Asgard.Hermod.Sync.Tests;

/// <summary>受控 HTTP 发行者，签发真实 RS256 JWT 并验证 SDK 的 PKCE 兑换。</summary>
internal sealed class OAuthTestProtocol : HttpMessageHandler, ILoginBrowser
{
    private readonly RSA _rsa = RSA.Create(2048);
    private HeimdallLoginRequest? _login;
    /// <summary>固定注册边界，测试不访问外网。</summary>
    public HermodIdentityOptions Options { get; } = new()
    {
        Authority = "https://issuer.test/tenant",
        TenantId = Guid.NewGuid().ToString(),
        ApplicationId = Guid.NewGuid().ToString(),
        ClientId = "desktop",
        ApiBaseUrl = "https://sync.test",
    };
    /// <summary>下一次浏览器登录取消。</summary>
    public bool CancelLogin { get; set; }
    /// <summary>下一次兑换模拟发行者拒绝。</summary>
    public bool RejectTokens { get; set; }
    /// <summary>模拟刷新发生身份改变。</summary>
    public bool ChangeSubject { get; set; }
    /// <summary>记录 PKCE verifier 确实来自原事务。</summary>
    public bool VerifiedPkce { get; private set; }
    /// <summary>记录令牌轮换和撤销次数。</summary>
    public int Refreshes { get; private set; }
    /// <summary>普通退出应只撤销本 Client 的 AT/RT。</summary>
    public int Revocations { get; private set; }
    /// <inheritdoc />
    public Task<IReadOnlyList<KeyValuePair<string, string>>> LoginAsync(HeimdallLoginRequest request, CancellationToken cancellationToken)
    {
        if (CancelLogin) throw new OperationCanceledException(cancellationToken);
        _login = request;
        return Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>([new("code", "authorization-code"), new("state", request.State)]);
    }
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("openid-configuration", StringComparison.Ordinal)) return Response(new
        {
            issuer = Options.Authority,
            authorization_endpoint = Options.Authority + "/authorize",
            token_endpoint = Options.Authority + "/token",
            jwks_uri = Options.Authority + "/jwks",
            revocation_endpoint = Options.Authority + "/revoke",
        });
        if (path.EndsWith("/jwks", StringComparison.Ordinal))
        {
            var parameters = _rsa.ExportParameters(false);
            return Response(new
            {
                keys = new[] { new { kty = "RSA", kid = "key", alg = "RS256", use = "sig",
                n = Base64UrlEncoder.Encode(parameters.Modulus), e = Base64UrlEncoder.Encode(parameters.Exponent) } }
            });
        }
        if (path.EndsWith("/revoke", StringComparison.Ordinal)) { Revocations++; return Response(new { }); }
        if (!path.EndsWith("/token", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected endpoint.");
        if (RejectTokens) return Response(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
        var fields = LoopbackLoginBrowser.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken)).ToDictionary();
        if (fields["grant_type"] == "authorization_code")
        {
            Assert.Equal(_login!.CodeVerifier, fields["code_verifier"]); Assert.Equal("authorization-code", fields["code"]);
            VerifiedPkce = true;
        }
        else { Refreshes++; Assert.Equal("refresh-credential", fields["refresh_token"]); }
        string subject = ChangeSubject ? "other-user" : "user-a";
        return Response(new
        {
            access_token = Jwt(subject, false),
            id_token = Jwt(subject, true),
            refresh_token = "refresh-credential",
            token_type = "Bearer",
            expires_in = 300,
            scope = Options.Scope,
        });
    }
    /// <summary>ID 和 AT 绑定同一主体与 sid；用户名与邮箱特意不同。</summary>
    private string Jwt(string subject, bool idToken)
    {
        var claims = new List<Claim> { new("sub", subject), new("sid", "session-a"), new("name", "JWT-username"),
            new("email", "different@example.com"), new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64) };
        if (idToken) claims.Add(new("nonce", _login!.Nonce));
        else claims.AddRange([new("token_type", "UserLogin"), new("client_id", Options.ClientId),
            new("tenant_id", Options.TenantId), new("application_id", Options.ApplicationId)]);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Options.Authority, idToken ? Options.ClientId : Options.Audience,
            claims, DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "key" }, SecurityAlgorithms.RsaSha256)));
    }
    /// <summary>构造受控协议包络，不模拟客户端内部实现。</summary>
    private static HttpResponseMessage Response(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };
    /// <inheritdoc />
    protected override void Dispose(bool disposing) { if (disposing) _rsa.Dispose(); base.Dispose(disposing); }
}
