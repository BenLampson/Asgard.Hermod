using System.Globalization;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.Sync.Tests;

/// <summary>真实 MVC 管线验证匿名注册、输入边界、限流和桌面安全错误转换。</summary>
public sealed class AccountRegistrationApiTests
{
    /// <summary>注册不需要已有用户令牌，成功响应不包含密码、角色或 Token。</summary>
    [Fact]
    public async Task AnonymousRegistration_ReturnsOnlyNormalizedUsername()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync();
        using var response = await host.Client.PostAsJsonAsync("/api/account/register", Valid(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(200, body.GetProperty("code").GetInt32());
        Assert.Equal("new-user", body.GetProperty("data").GetProperty("username").GetString());
        Assert.Single(body.GetProperty("data").EnumerateObject());
        Assert.DoesNotContain(Valid().Password, body.GetRawText());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
    }
    /// <summary>拒绝角色和租户字段，不能把任意输入透传给 Heimdall。</summary>
    [Theory]
    [InlineData("tenantId")]
    [InlineData("applicationId")]
    [InlineData("roles")]
    [InlineData("status")]
    public async Task Registration_RejectsExtraFieldsBeforeCallingUpstream(string field)
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync();
        var payload = new Dictionary<string, object>
        {
            ["username"] = "new-user",
            ["email"] = "new-user@example.test",
            ["password"] = Valid().Password,
            [field] = "injected"
        };
        using var response = await host.Client.PostAsJsonAsync("/api/account/register", payload, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, host.Registration.Calls);
        Assert.DoesNotContain("injected", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
    /// <summary>明确的失败只渲染本地资源，英文和中文都不能泄漏服务端异常。</summary>
    [Theory]
    [InlineData("en", 409, "RegistrationConflict")]
    [InlineData("zh-Hans", 409, "RegistrationConflict")]
    [InlineData("en", 429, "RegistrationRateLimited")]
    [InlineData("zh-Hans", 503, "RegistrationUnavailable")]
    [InlineData("en", 502, "RegistrationOutcomeUnknown")]
    public async Task DesktopRegistration_MapsStableStatusToLocalResource(string language, int status, string key)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            await using var host = new ApiTestHost(); await host.InitializeAsync();
            host.Registration.Error = new HermodApiException(status, key);
            var service = new AccountRegistrationService(new SyncTestClientFactory(host));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(Valid(), TestContext.Current.CancellationToken));
            Assert.Equal(key, error.Data["Hermod.ResourceKey"]); Assert.Equal(Text.Get(key), Text.Error(error));
            Assert.Equal(1, host.Registration.Calls);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
    /// <summary>全局注册预算耗尽后返回 429，既有认证端点仍正常拒绝匿名访问。</summary>
    [Fact]
    public async Task Registration_RateBudgetDoesNotAffectExistingApi()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync();
        for (int index = 0; index < 20; index++)
        {
            using var response = await host.Client.PostAsJsonAsync("/api/account/register", Valid(), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var limited = await host.Client.PostAsJsonAsync("/api/account/register", Valid(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(20, host.Registration.Calls); Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
    }
    /// <summary>调用端与 API 使用相同的长度和格式边界。</summary>
    [Theory]
    [InlineData("", "valid@example.test", "long-password", "RegistrationInvalidUsername")]
    [InlineData("bad\nname", "valid@example.test", "long-password", "RegistrationInvalidUsername")]
    [InlineData("valid", "invalid", "long-password", "RegistrationInvalidEmail")]
    [InlineData("valid", "valid@example.test", "short", "RegistrationInvalidPassword")]
    public void Registration_ValidatesInput(string username, string email, string password, string resource)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new AccountRegistrationRequest
        { Username = username, Email = email, Password = password }.Validate());
        Assert.Equal(resource, error.Data["Hermod.ResourceKey"]);
    }
    /// <summary>真实 Kestrel 的请求大小拒绝必须保留 413，不能被统一异常处理改成 500。</summary>
    [Fact]
    public async Task RequestSizeFailure_PreservesStatusWithoutExposingDiagnostics()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Path = "/api/account/register";
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        var middleware = new HermodApiErrorMiddleware(_ => throw new Microsoft.AspNetCore.Http.BadHttpRequestException("private diagnostics", 413));
        await middleware.InvokeAsync(context);
        Assert.Equal(413, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(413, body.RootElement.GetProperty("code").GetInt32());
        Assert.DoesNotContain("private", body.RootElement.GetRawText());
    }
    /// <summary>只生成测试内存资料，不写本机配置。</summary>
    private static AccountRegistrationRequest Valid() => new()
    { Username = " new-user ", Email = "new-user@example.test", Password = "test-password-123456" };
}
