using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Asgard.Hermod.Identity;

/// <summary>先独占监听固定回环回调，再打开系统浏览器；窗口关闭或超时释放监听。</summary>
public sealed class LoopbackLoginBrowser : ILoginBrowser
{
    private readonly Action<string> _openBrowser;

    /// <summary>生产环境通过 Windows 默认浏览器打开授权页。</summary>
    public LoopbackLoginBrowser() : this(url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }

    /// <summary>回环集成测试只替换外部浏览器启动，仍执行真实监听及响应。</summary>
    internal LoopbackLoginBrowser(Action<string> openBrowser) => _openBrowser = openBrowser;

    /// <inheritdoc />
    public Task<IReadOnlyList<KeyValuePair<string, string>>> LoginAsync(HeimdallLoginRequest request, CancellationToken cancellationToken) =>
        ReceiveAsync(request, null, cancellationToken);

    /// <inheritdoc />
    public async Task AuthenticateAsync(HeimdallLoginRequest request,
        Func<IReadOnlyList<KeyValuePair<string, string>>, CancellationToken, Task> authenticate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authenticate);
        await ReceiveAsync(request, authenticate, cancellationToken);
    }

    /// <summary>持有一次性回调响应直到身份提交完成，避免把收到授权码误报为登录成功。</summary>
    private async Task<IReadOnlyList<KeyValuePair<string, string>>> ReceiveAsync(HeimdallLoginRequest request,
        Func<IReadOnlyList<KeyValuePair<string, string>>, CancellationToken, Task>? authenticate, CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(request.RedirectUri);
        listener.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        _openBrowser(request.Url);
        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            var incoming = context.Request;
            var values = ParseQuery(incoming.Url?.Query ?? "");
            bool matches = incoming.HttpMethod == "GET" && incoming.Url?.AbsolutePath == new Uri(request.RedirectUri).AbsolutePath
                && incoming.RemoteEndPoint?.Address.Equals(IPAddress.Loopback) == true
                && values.Count(pair => pair.Key == "state") == 1
                && values.Any(pair => pair.Key == "state" && pair.Value == request.State);
            if (!matches)
            {
                await ReplyAsync(context.Response, LoginCallbackStatus.Invalid);
                continue;
            }
            try
            {
                if (authenticate is not null) await authenticate(values, timeout.Token);
            }
            catch
            {
                await ReplyAsync(context.Response, LoginCallbackStatus.Failed);
                throw;
            }
            await ReplyAsync(context.Response, authenticate is null ? LoginCallbackStatus.Received : LoginCallbackStatus.Succeeded);
            return values; // 本次监听随返回立即释放，同一事务不能再次兑换。
        }
    }

    /// <summary>结果页禁止缓存或外部请求；用户提前关页不能撤销已提交的登录。</summary>
    private static async Task ReplyAsync(HttpListenerResponse response, LoginCallbackStatus status)
    {
        try
        {
            string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            byte[] body = Encoding.UTF8.GetBytes(LoginCallbackPage.Render(status, nonce));
            response.StatusCode = status == LoginCallbackStatus.Invalid ? 400 : 200;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = body.Length;
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Content-Security-Policy"] = $"default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; img-src data:; style-src 'nonce-{nonce}'; script-src 'nonce-{nonce}'";
            await response.OutputStream.WriteAsync(body);
        }
        catch (Exception error) when (error is HttpListenerException or IOException or ObjectDisposedException)
        {
            // 浏览器中断响应只影响展示，不改变账号服务的验证及持久化结果。
        }
        finally
        {
            try { response.Close(); }
            catch (Exception error) when (error is HttpListenerException or ObjectDisposedException)
            {
                // 已中断的回调连接不影响此前已经验证并保存的身份。
            }
        }
    }

    /// <summary>保留重复字段交由 SDK 拒绝，不能在解析时覆盖 state 或 code。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ParseQuery(string query) => query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries).Select(value =>
        {
            string[] pair = value.Split('=', 2);
            return new KeyValuePair<string, string>(WebUtility.UrlDecode(pair[0]), WebUtility.UrlDecode(pair.Length == 2 ? pair[1] : ""));
        }).ToArray();
}
