namespace Asgard.Hermod.Abstractions.Services;

/// <summary>桌面登录生命周期；凭据只交给协议和资源调用层。</summary>
public interface IAccountService
{
    /// <summary>当前经过验签的用户，未登录时为空。</summary>
    HermodAccount? Account { get; }
    /// <summary>身份提交或退出后通知界面。</summary>
    event EventHandler? AccountChanged;
    /// <summary>恢复当前设备保护的会话。</summary>
    Task RestoreAsync(CancellationToken cancellationToken = default);
    /// <summary>使用系统浏览器登录，取消时保留原会话。</summary>
    Task SignInAsync(CancellationToken cancellationToken = default);
    /// <summary>清除本应用凭据并撤销原授权。</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
    /// <summary>为资源请求取得未过期的用户令牌，必要时轮换刷新。</summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    /// <summary>在捕获的会话范围内完成操作，期间不允许登录或退出改变数据归属。</summary>
    Task RunAuthenticatedAsync(Func<HermodAccount, string, Task> action, CancellationToken cancellationToken = default);
}
