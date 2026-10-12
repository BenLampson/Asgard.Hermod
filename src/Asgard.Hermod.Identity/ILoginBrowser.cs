namespace Asgard.Hermod.Identity;

/// <summary>为单次 PKCE 事务提供系统浏览器和一次性回调。</summary>
public interface ILoginBrowser
{
    /// <summary>回调仅返回原始协议字段；身份仍由官方 SDK 验证。</summary>
    Task<IReadOnlyList<KeyValuePair<string, string>>> LoginAsync(HeimdallLoginRequest request, CancellationToken cancellationToken);
}
