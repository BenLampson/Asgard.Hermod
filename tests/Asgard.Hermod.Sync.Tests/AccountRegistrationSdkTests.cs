using Asgard.Heimdall.Client;
using Asgard.Hermod.Server.Services;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.Sync.Tests;

/// <summary>真实官方 C# SDK 的凭据、签名、Scope、缓存与不重试创建请求的边界。</summary>
public sealed class AccountRegistrationSdkTests
{
    /// <summary>连续创建复用已验证的服务 Token，桌面资料不能决定租户和角色。</summary>
    [Fact]
    public async Task OfficialSdk_CreatesUsersWithDedicatedServiceScope()
    {
        using var protocol = new RegistrationSdkProtocol();
        await using var provider = Build(protocol);
        var service = provider.GetRequiredService<IAccountRegistrationService>();
        Assert.Equal("new-user", (await service.RegisterAsync(Request(), TestContext.Current.CancellationToken)).Username);
        await service.RegisterAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(1, protocol.Tokens); Assert.Equal(2, protocol.Creations);
    }
    /// <summary>上游拒绝保留稳定状态，不泄漏其原始正文，也不自动重发 POST。</summary>
    [Theory]
    [InlineData(400, 400, "RegistrationInvalidRequest")]
    [InlineData(409, 409, "RegistrationConflict")]
    [InlineData(403, 503, "RegistrationUnavailable")]
    [InlineData(429, 429, "RegistrationRateLimited")]
    [InlineData(500, 502, "RegistrationOutcomeUnknown")]
    public async Task OfficialSdk_MapsFailuresWithoutRetry(int upstreamStatus, int expectedStatus, string resource)
    {
        using var protocol = new RegistrationSdkProtocol { Status = (HttpStatusCode)upstreamStatus };
        await using var provider = Build(protocol);
        var error = await Assert.ThrowsAsync<HermodApiException>(() => provider.GetRequiredService<IAccountRegistrationService>()
            .RegisterAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal(expectedStatus, error.StatusCode); Assert.Equal(resource, error.ResourceKey);
        Assert.DoesNotContain("sensitive", error.Message); Assert.Equal(1, protocol.Creations);
    }
    /// <summary>网络断开或归属错误均不能当成功，也不能盲目重复创建。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OfficialSdk_RejectsUncertainOrUntrustedResult(bool disconnect)
    {
        using var protocol = new RegistrationSdkProtocol { Disconnect = disconnect, WrongTenant = !disconnect };
        await using var provider = Build(protocol);
        var error = await Assert.ThrowsAsync<HermodApiException>(() => provider.GetRequiredService<IAccountRegistrationService>()
            .RegisterAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal("RegistrationOutcomeUnknown", error.ResourceKey); Assert.Equal(1, protocol.Creations);
        Assert.DoesNotContain("secret", Text.Get(error.ResourceKey));
    }
    /// <summary>保留 SDK 自带的 HTTP 管线，仅替换远程发行者边界。</summary>
    private static ServiceProvider Build(RegistrationSdkProtocol protocol)
    {
        var services = new ServiceCollection();
        services.AddOptions<HermodServerOptions>().Configure(options =>
        {
            options.ServiceClientId = protocol.Options.ServiceClientId;
            options.ServiceClientSecret = protocol.Options.ServiceClientSecret;
        });
        services.AddSingleton<IHeimdallCredentialProvider, ServerCredentialProvider>();
        services.AddHeimdallClient("hermod-registration", options =>
        {
            options.Authority = protocol.Options.Authority; options.ApiBaseUrl = protocol.Options.IdentityApiBaseUrl;
            options.TenantId = protocol.Options.TenantId; options.ApplicationId = protocol.Options.ApplicationId;
            options.Scope = "heimdall.users.create"; options.Audience = "heimdall-users-api";
        });
        services.AddHttpClient("heimdall.protocol.hermod-registration").ConfigurePrimaryHttpMessageHandler(() => protocol);
        services.AddHttpClient("heimdall.api.hermod-registration").ConfigurePrimaryHttpMessageHandler(() => protocol);
        services.AddScoped<IAccountRegistrationService, HeimdallAccountRegistrationService>();
        return services.BuildServiceProvider();
    }
    /// <summary>仅内存中的测试账号。</summary>
    private static AccountRegistrationRequest Request() => new()
    { Username = " new-user ", Email = "new-user@example.test", Password = "test-password-123456" };
}
