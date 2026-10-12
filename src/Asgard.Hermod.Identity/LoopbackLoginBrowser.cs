using System.Diagnostics;
using System.Net;
using System.Text;

namespace Asgard.Hermod.Identity;

/// <summary>先独占监听固定回环回调，再打开系统浏览器；窗口关闭或超时释放监听。</summary>
public sealed class LoopbackLoginBrowser : ILoginBrowser
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<KeyValuePair<string, string>>> LoginAsync(HeimdallLoginRequest request, CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(request.RedirectUri);
        listener.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        Process.Start(new ProcessStartInfo(request.Url) { UseShellExecute = true });
        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            var incoming = context.Request;
            var values = ParseQuery(incoming.Url?.Query ?? "");
            bool matches = incoming.HttpMethod == "GET" && incoming.Url?.AbsolutePath == new Uri(request.RedirectUri).AbsolutePath
                && incoming.RemoteEndPoint?.Address.Equals(IPAddress.Loopback) == true
                && values.Count(pair => pair.Key == "state") == 1
                && values.Any(pair => pair.Key == "state" && pair.Value == request.State);
            context.Response.StatusCode = matches ? 200 : 400;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
            byte[] body = Encoding.UTF8.GetBytes(Text.Get(matches ? "LoginCallbackReceived" : "LoginCallbackInvalid"));
            await context.Response.OutputStream.WriteAsync(body, timeout.Token);
            context.Response.Close();
            if (matches) return values; // 本次监听随返回立即释放，同一事务不能再次兑换。
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
