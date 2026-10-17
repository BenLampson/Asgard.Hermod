using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Asgard.AspNetCore.Core;
using Asgard.AspNetCore.Core.Data;
using Asgard.Core;
using Asgard.Core.AsgardContextModule;
using Asgard.Hermod.Server.Controllers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Asgard.Hermod.Sync.Tests;

/// <summary>真实 MVC、JWT 验签、Asgard 身份上下文和 SQLite 的隔离测试宿主。</summary>
internal sealed class ApiTestHost : IAsyncDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HermodSyncTests", Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    /// <summary>每个用例使用独立应用与租户。</summary>
    public HermodServerOptions Options { get; } = new()
    {
        Authority = "https://issuer.test/tenant",
        TenantId = Guid.NewGuid().ToString(),
        ApplicationId = Guid.NewGuid().ToString(),
        DesktopClientId = "desktop-client",
        ServiceClientId = "server-client",
        ServiceClientSecret = new string('S', 48),
        IdentityApiBaseUrl = "https://issuer.test",
    };
    /// <summary>在线目录测试边界。</summary>
    public TestUserAccessVerifier Verifier { get; } = new();
    /// <summary>注册 API 的隔离上游服务。</summary>
    public TestAccountRegistrationService Registration { get; } = new();
    /// <summary>实际 HTTP 客户端。</summary>
    public HttpClient Client { get; private set; } = null!;
    /// <summary>同步客户端工厂取得独立可释放 HTTP 客户端。</summary>
    internal HttpClient CreateClient() => _app.GetTestClient();
    /// <summary>创建最小请求管线，生产控制器和业务服务保持原样。</summary>
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory); Options.DatabasePath = Path.Combine(_directory, "sync.db");
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        builder.Services.AddSingleton<ICloudWorkspaceRepository, SqliteCloudWorkspaceRepository>();
        builder.Services.AddSingleton<IUserAccessVerifier>(Verifier);
        builder.Services.AddSingleton<IAccountRegistrationService>(Registration);
        builder.Services.AddAccountRegistrationRateLimiting();
        builder.Services.AddScoped<CurrentHermodUser>(); builder.Services.AddScoped<WorkspaceSyncApplicationService>();
        builder.Services.AddHttpContextAccessor(); builder.Services.AddAsgardAspNetCore(); builder.Services.AddAsgardContext();
        builder.Services.AddControllers().AddApplicationPart(typeof(AccountController).Assembly)
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = _ =>
                new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(Asgard.Abstractions.AspNetCore.Model.Response.Fail<object>(400,
                    Asgard.Hermod.Abstractions.Localization.Text.Get("RegistrationInvalidRequest"))));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new()
            {
                ValidIssuer = Options.Authority,
                ValidAudience = "hermod-api",
                IssuerSigningKey = new RsaSecurityKey(_rsa),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ClockSkew = TimeSpan.Zero,
            };
        });
        _app = builder.Build(); _app.UseAuthentication(); _app.UseAsgardTenant();
        _app.UseMiddleware<HermodApiErrorMiddleware>(); _app.UseRateLimiter(); _app.UseAuthorization(); _app.MapControllers();
        _app.Services.GetRequiredService<ICloudWorkspaceRepository>().Initialize();
        await _app.StartAsync(); Client = _app.GetTestClient();
    }
    /// <summary>生成真实签名用户 JWT，允许覆盖边界测试字段。</summary>
    public string Token(string subject = "user-a", string username = "login-name", string? tenant = null,
        string? application = null, string type = "UserLogin", string? client = null, string audience = "hermod-api", bool expired = false)
    {
        var claims = new[] { new Claim("sub", subject), new Claim("user_id", subject), new Claim("name", username),
            new Claim("tenant_id", tenant ?? Options.TenantId), new Claim("application_id", application ?? Options.ApplicationId),
            new Claim("client_id", client ?? Options.DesktopClientId), new Claim("token_type", type),
            new Claim("scope", "[\"hermod.sync\"]"), new Claim("permissions", "[\"hermod.sync\"]") };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Options.Authority, audience, claims,
            notBefore: DateTime.UtcNow.AddMinutes(-10), expires: DateTime.UtcNow.AddMinutes(expired ? -1 : 5),
            signingCredentials: new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256)));
    }
    /// <summary>只为当前 HTTP 请求设置捕获的 Bearer。</summary>
    public void SignIn(string token) => Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client?.Dispose(); if (_app is not null) await _app.DisposeAsync(); _rsa.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
