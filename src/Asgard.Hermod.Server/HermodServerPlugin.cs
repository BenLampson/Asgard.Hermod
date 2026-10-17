using Asgard.Hermod.Abstractions.Services;
using Asgard.Hermod.Server.Services;
using Asgard.PluginSdk;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;

namespace Asgard.Hermod.Server;

/// <summary>同步插件拥有业务服务和数据库；宿主管理认证与生命周期。</summary>
public sealed class HermodServerPlugin : PluginBase
{
    /// <inheritdoc />
    public override string Id => "hermod-sync";
    /// <inheritdoc />
    public override string Name => "Hermod Sync";
    /// <inheritdoc />
    public override Version Version => new(0, 1, 0);
    /// <inheritdoc />
    protected override Task OnConfigureServicesAsync(IPluginServiceConfigurationContext context, CancellationToken cancellationToken)
    {
        var services = context.Services;
        services.AddHttpContextAccessor();
        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = action =>
            new BadRequestObjectResult(Response.Fail<object>(400, Text.Get(action.HttpContext.Request.Path == "/api/account/register"
                ? "RegistrationInvalidRequest" : "SyncSnapshotInvalid"))));
        services.AddAccountRegistrationRateLimiting();
        services.AddOptions<HermodServerOptions>().BindConfiguration("hermodServer")
            .Validate(options => options.IsValid(), "Hermod server configuration is invalid.").ValidateOnStart();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<IOptions<HermodServerOptions>>((jwt, configured) =>
            {
                var previous = jwt.Events.OnTokenValidated;
                // 宿主模板可以匹配多个租户；Hermod 必须额外固定精确发行者，
                // 防止其他租户自有签名密钥伪造此应用的 tenant_id、client_id 与 sub。
                jwt.Events.OnTokenValidated = async authentication =>
                {
                    if (authentication.SecurityToken.Issuer != configured.Value.Authority)
                    {
                        authentication.Fail(Text.Get("SyncForbidden"));
                        return;
                    }
                    await previous(authentication);
                };
            });
        // 命名 SDK 只负责服务 Client 的只读目录，不复用桌面用户令牌。
        services.AddHeimdallClient("hermod-directory", _ => { });
        services.AddOptions<HeimdallClientOptions>("hermod-directory").Configure<IConfiguration>((options, configuration) =>
        {
            var server = configuration.GetSection("hermodServer").Get<HermodServerOptions>()!;
            options.Authority = server.Authority; options.ApiBaseUrl = server.IdentityApiBaseUrl;
            options.TenantId = server.TenantId; options.ApplicationId = server.ApplicationId;
        });
        services.AddHeimdallClient("hermod-registration", _ => { });
        services.AddOptions<HeimdallClientOptions>("hermod-registration").Configure<IConfiguration>((options, configuration) =>
        {
            var server = configuration.GetSection("hermodServer").Get<HermodServerOptions>()!;
            options.Authority = server.Authority; options.ApiBaseUrl = server.IdentityApiBaseUrl;
            options.TenantId = server.TenantId; options.ApplicationId = server.ApplicationId;
            options.Scope = "heimdall.users.create"; options.Audience = "heimdall-users-api";
        });
        services.AddSingleton<IHeimdallCredentialProvider, ServerCredentialProvider>();
        services.AddScoped<IAccountRegistrationService, HeimdallAccountRegistrationService>();
        services.AddSingleton<ICloudWorkspaceRepository, SqliteCloudWorkspaceRepository>();
        services.AddScoped<IUserAccessVerifier, HeimdallUserAccessVerifier>();
        services.AddScoped<CurrentHermodUser>();
        services.AddScoped<WorkspaceSyncApplicationService>();
        return Task.CompletedTask;
    }
    /// <inheritdoc />
    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        GetService<ICloudWorkspaceRepository>().Initialize();
        return Task.CompletedTask;
    }
    /// <inheritdoc />
    protected override Task OnConfigureMiddlewareAsync(IPluginMiddlewareConfigurationContext context, CancellationToken cancellationToken)
    {
        context.App.UseMiddleware<HermodApiErrorMiddleware>();
        context.App.UseRateLimiter();
        return Task.CompletedTask;
    }
}
