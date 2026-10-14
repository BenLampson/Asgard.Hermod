namespace Asgard.Hermod.UI.Windowing;

/// <summary>把实际导航打开请求路由到独立工作窗口，保持主窗体首页与模块边界。</summary>
public sealed class WorkspaceWindowHost : IDisposable
{
    private readonly WorkspaceViewModel _workspace;
    private readonly Dictionary<WorkspacePageViewModel, WorkspaceWindow> _windows = [];

    /// <summary>在主窗体构造前接管工作页呈现，重复打开同一实例只激活原窗口。</summary>
    public WorkspaceWindowHost(WorkspaceViewModel workspace)
    {
        _workspace = workspace;
        workspace.UseIndependentWindows = true;
        workspace.PageActivated += Open;
    }

    /// <summary>真实工作窗口快照，用于全应用关闭预检。</summary>
    public IReadOnlyList<WorkspaceWindow> Windows => _windows.Values.ToArray();

    /// <summary>不设置主窗体 Owner，使工作窗口保持独立的可见性和任务栏入口。</summary>
    private void Open(WorkspacePageViewModel page)
    {
        if (!_windows.TryGetValue(page, out var window))
        {
            window = new WorkspaceWindow(_workspace, page);
            _windows.Add(page, window);
            window.Closed += (_, _) => _windows.Remove(page);
        }
        WindowActivation.Show(window);
    }

    /// <summary>页面由工作区异步释放；承载服务只解除导航订阅。</summary>
    public void Dispose() => _workspace.PageActivated -= Open;
}
