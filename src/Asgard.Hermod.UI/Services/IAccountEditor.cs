namespace Asgard.Hermod.UI.Services;

/// <summary>账号窗口入口，外壳不依赖登录协议和同步实现。</summary>
public interface IAccountEditor
{
    /// <summary>打开当前用户的登录与同步界面。</summary>
    Task OpenAsync();
    /// <summary>从设置等子窗口进入时，账号弹窗归属直接打开者。</summary>
    Task OpenAsync(Window? owner) => OpenAsync();
    /// <summary>直接进入冲突处理；取消保留双方配置，旧实现退回账户管理。</summary>
    Task OpenConflictsAsync(Window? owner = null) => OpenAsync(owner);
    /// <summary>未登录入口先展示登录与注册选择，由用户明确发起浏览器登录。</summary>
    Task OpenForSignInAsync() => OpenAsync();
}
