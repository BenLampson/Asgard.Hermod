using System.Globalization;
using System.Net.Sockets;
using Asgard.Heimdall.Client;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.Sync.Tests;

/// <summary>真实回环 HTTP 集成验证结果页时序、失败保护和事务隔离；不打开外部浏览器。</summary>
public sealed class LoopbackLoginBrowserTests
{
    /// <summary>响应成功只能发生在身份验证和保存完成后，中英和未支持语言均正确显示。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public async Task SuccessPage_WaitsForAuthenticationAndLocalizes(string language)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var protocol = new OAuthTestProtocol(); using var protocolHttp = new HttpClient(protocol);
            protocol.Options.RedirectUri = FreeRedirectUri();
            using var oauth = new HeimdallOAuthClient(protocol.Options.ToOAuthOptions(), httpClient: protocolHttp);
            var request = await oauth.CreateLoginRequestAsync(cancellationToken: TestContext.Current.CancellationToken);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var browser = new LoopbackLoginBrowser(_ => started.SetResult());
            var login = browser.AuthenticateAsync(request, async (values, token) =>
            {
                Assert.Contains(values, pair => pair.Key == "code" && pair.Value == "private-code");
                entered.SetResult(); await committed.Task.WaitAsync(token);
            }, TestContext.Current.CancellationToken);
            await started.Task;
            using var http = new HttpClient();
            var responseTask = http.GetAsync(request.RedirectUri + "?code=private-code&state=" + Uri.EscapeDataString(request.State), TestContext.Current.CancellationToken);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(responseTask.IsCompleted);
            committed.SetResult(); await login;
            using var response = await responseTask;
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(Text.Get("LoginCallbackSuccessTitle"), WebUtility.HtmlDecode(body));
            Assert.Contains("data-state=\"success\"", body);
            Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
            Assert.True(response.Headers.ConnectionClose);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Contains("script-src 'nonce-", response.Headers.GetValues("Content-Security-Policy").Single());
            Assert.DoesNotContain("private-code", body); Assert.DoesNotContain(request.State, body);
            Assert.Contains("history.replaceState", body); Assert.Contains("window.close()", body);
            Assert.Contains("data:image/png;base64,", body);
            // 可选预览只保存脱敏结果页，供真实浏览器检查排版及关闭按钮。
            if (Environment.GetEnvironmentVariable("HERMOD_CALLBACK_PREVIEW_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, language + ".html"), body, TestContext.Current.CancellationToken);
            }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    /// <summary>错误回调不消费当前事务；失败页不展示 SDK 原始错误或假称成功。</summary>
    [Fact]
    public async Task InvalidState_DoesNotConsumeLogin_AndFailedAuthenticationIsReportedSafely()
    {
        using var protocol = new OAuthTestProtocol(); using var protocolHttp = new HttpClient(protocol);
        protocol.Options.RedirectUri = FreeRedirectUri();
        using var oauth = new HeimdallOAuthClient(protocol.Options.ToOAuthOptions(), httpClient: protocolHttp);
        var request = await oauth.CreateLoginRequestAsync(cancellationToken: TestContext.Current.CancellationToken);
        int authentications = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = new LoopbackLoginBrowser(_ => started.SetResult());
        var login = browser.AuthenticateAsync(request, (_, _) =>
        {
            authentications++; throw new IOException("raw-secret-sdk-response");
        }, TestContext.Current.CancellationToken);
        await started.Task;
        using var http = new HttpClient();
        using var invalid = await http.GetAsync(request.RedirectUri + "?code=private-code&state=wrong", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.Equal(0, authentications); Assert.False(login.IsCompleted);
        using var failure = await http.GetAsync(request.RedirectUri + "?code=private-code&state=" + Uri.EscapeDataString(request.State), TestContext.Current.CancellationToken);
        string body = await failure.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => login);
        Assert.Equal(1, authentications);
        Assert.Contains("data-state=\"failure\"", body); Assert.DoesNotContain("data-state=\"success\"", body);
        Assert.DoesNotContain("raw-secret", body); Assert.DoesNotContain("private-code", body);
    }

    /// <summary>测试为每次监听分配独立回环端口，避免依赖生产回调端口或用户的应用状态。</summary>
    private static string FreeRedirectUri()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        return $"http://127.0.0.1:{((IPEndPoint)socket.LocalEndpoint).Port}/callback/";
    }
}
