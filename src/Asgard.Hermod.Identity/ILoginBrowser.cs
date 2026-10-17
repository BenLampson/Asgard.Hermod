namespace Asgard.Hermod.Identity;

/// <summary>为单次 PKCE 事务提供系统浏览器和一次性回调。</summary>
public interface ILoginBrowser
{
    /// <summary>回调仅返回原始协议字段；身份仍由官方 SDK 验证。</summary>
    Task<IReadOnlyList<KeyValuePair<string, string>>> LoginAsync(HeimdallLoginRequest request, CancellationToken cancellationToken);

    /// <summary>验证并保存身份后才确认成功；无结果页的浏览器实现仍复用原始回调入口。</summary>
    async Task AuthenticateAsync(HeimdallLoginRequest request,
        Func<IReadOnlyList<KeyValuePair<string, string>>, CancellationToken, Task> authenticate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authenticate);
        var callback = await LoginAsync(request, cancellationToken);
        await authenticate(callback, cancellationToken);
    }
}
