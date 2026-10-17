using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>SSH 宿主只转发显式入口；日志窗口独立拥有快照文档，不挤占监测正文。</summary>
public partial class SshWorkspaceView
{
    private readonly Dictionary<SshContainerLogViewModel, SshContainerLogWindow> _logWindows = [];

    private void OnLogWindowRequested(object? sender, SshContainerLogViewModel document)
    {
        if (_disposed || sender is not SshWorkspaceViewModel workspace) return;
        if (_logWindows.TryGetValue(document, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Show(); existing.Activate(); return;
        }
        var window = new SshContainerLogWindow(document);
        _logWindows.Add(document, window);
        window.Closed += (_, _) => { _logWindows.Remove(document); workspace.ReleaseLogDocument(document); };
        window.Show();
    }

    /// <summary>销毁宿主只解除窗口索引，独立阅读器继续保留已取得的快照。</summary>
    private void CloseLogWindows()
    {
        _logWindows.Clear();
    }
}
