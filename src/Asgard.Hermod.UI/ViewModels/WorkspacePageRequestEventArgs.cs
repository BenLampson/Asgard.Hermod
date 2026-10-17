namespace Asgard.Hermod.UI.ViewModels;

/// <summary>模块请求按实例内稳定上下文打开窗口；工厂只在尚无对应页面时调用。</summary>
public sealed class WorkspacePageRequestEventArgs(string? contextId, Func<WorkspacePageViewModel> createPage) : EventArgs
{
    /// <summary>空值代表实例入口，其他标识由模块定义且不依赖翻译。</summary>
    public string? ContextId { get; } = contextId;
    /// <summary>创建尚未连接的页面，生命周期仍由公共工作区统一管理。</summary>
    public Func<WorkspacePageViewModel> CreatePage { get; } = createPage;
}
