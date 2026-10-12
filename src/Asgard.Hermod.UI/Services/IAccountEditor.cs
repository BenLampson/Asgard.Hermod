namespace Asgard.Hermod.UI.Services;

/// <summary>账号窗口入口，外壳不依赖登录协议和同步实现。</summary>
public interface IAccountEditor
{
    /// <summary>打开当前用户的登录与同步界面。</summary>
    Task OpenAsync();
}
