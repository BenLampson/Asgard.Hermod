namespace Asgard.Hermod.UI.Services;

/// <summary>账号窗口入口，外壳不依赖登录协议和同步实现。</summary>
public interface IAccountEditor
{
    /// <summary>打开当前用户的登录与同步界面。</summary>
    Task OpenAsync();
    /// <summary>未登录入口直接发起登录，同时保留可取消的账号窗口。</summary>
    Task OpenForSignInAsync() => OpenAsync();
}
