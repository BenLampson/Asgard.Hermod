using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>独立日志窗口由 SSH 宿主拥有，不在监测面板中挤占正文。</summary>
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
        if (Window.GetWindow(this) is { IsVisible: true } owner) window.Owner = owner;
        _logWindows.Add(document, window);
        window.Closed += (_, _) => { _logWindows.Remove(document); workspace.ReleaseLogDocument(document); };
        window.Show();
    }

    /// <summary>切换模型或销毁宿主时关闭其子窗口，释放读取和模型请求。</summary>
    private void CloseLogWindows()
    {
        foreach (var window in _logWindows.Values.ToArray()) window.Close();
        _logWindows.Clear();
    }
}
