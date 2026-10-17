using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Asgard.Hermod.Sync.Tests;

/// <summary>真实 RS256 服务令牌与模拟供给 API，验证 Hermod 确实经官方 SDK 调用。</summary>
internal sealed class RegistrationSdkProtocol : HttpMessageHandler
{
    private readonly RSA _rsa = RSA.Create(2048);
    /// <summary>固定测试身份，不访问生产。</summary>
    public HermodServerOptions Options { get; } = new()
    {
        Authority = "https://registration.test/tenant",
        IdentityApiBaseUrl = "https://registration.test",
        TenantId = Guid.NewGuid().ToString(),
        ApplicationId = Guid.NewGuid().ToString(),
        ServiceClientId = "server-client",
        ServiceClientSecret = new string('R', 48),
        DesktopClientId = "desktop",
    };
    /// <summary>供给响应状态。</summary>
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    /// <summary>成功包络模拟错误的租户归属。</summary>
    public bool WrongTenant { get; set; }
    /// <summary>模拟创建已发出后的连接中断。</summary>
    public bool Disconnect { get; set; }
    /// <summary>服务令牌申请和实际创建次数。</summary>
    public int Tokens { get; private set; }
    /// <summary>统计写请求以发现隐藏的自动重试。</summary>
    public int Creations { get; private set; }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("openid-configuration", StringComparison.Ordinal)) return Reply(new
        {
            issuer = Options.Authority,
            token_endpoint = Options.Authority + "/token",
            jwks_uri = Options.Authority + "/jwks",
        });
        if (path.EndsWith("/jwks", StringComparison.Ordinal))
        {
            var key = _rsa.ExportParameters(false);
            return Reply(new
            {
                keys = new[] { new { kty = "RSA", kid = "registration-key", alg = "RS256", use = "sig",
                n = Base64UrlEncoder.Encode(key.Modulus), e = Base64UrlEncoder.Encode(key.Exponent) } }
            });
        }
        if (path.EndsWith("/token", StringComparison.Ordinal))
        {
            Tokens++;
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            string basic = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!));
            Assert.Equal(Options.ServiceClientId + ":" + Options.ServiceClientSecret, basic);
            var form = LoopbackLoginBrowser.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken)).ToDictionary();
            Assert.Equal("client_credentials", form["grant_type"]); Assert.Equal("heimdall.users.create", form["scope"]);
            var claims = new[] { new Claim("sub", Options.ServiceClientId), new Claim("client_id", Options.ServiceClientId),
                new Claim("tenant_id", Options.TenantId), new Claim("application_id", Options.ApplicationId),
                new Claim("token_type", "BackendService"), new Claim("scope", "[\"heimdall.users.create\"]") };
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Options.Authority, "heimdall-users-api", claims,
                notBefore: DateTime.UtcNow.AddSeconds(-5), expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "registration-key" }, SecurityAlgorithms.RsaSha256)));
            return Reply(new { access_token = token, token_type = "Bearer", expires_in = 300 });
        }
        Assert.Equal("/api/backend/users", path); Assert.Equal(HttpMethod.Post, request.Method);
        Creations++;
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        var payload = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.False(payload.TryGetProperty("tenantId", out _)); Assert.False(payload.TryGetProperty("roleCodes", out _));
        if (Disconnect) throw new HttpRequestException("upstream secret or database details must stay hidden");
        if (Status != HttpStatusCode.OK) return Reply(new { code = (int)Status, message = "sensitive upstream diagnostics" }, Status);
        return Reply(new
        {
            code = 200,
            data = new
            {
                id = "created-user",
                tenantId = WrongTenant ? "other-tenant" : Options.TenantId,
                applicationId = Options.ApplicationId,
                username = payload.GetProperty("username").GetString(),
                email = payload.GetProperty("email").GetString(),
                roleCodes = new[] { "hermod.user" }
            }
        });
    }
    /// <summary>所有响应均走实际 JSON 解析。</summary>
    private static HttpResponseMessage Reply(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };
    /// <inheritdoc />
    protected override void Dispose(bool disposing) { if (disposing) _rsa.Dispose(); base.Dispose(disposing); }
}
