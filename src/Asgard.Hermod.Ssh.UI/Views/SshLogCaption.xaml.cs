using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>固定日志身份，独立 Agent 只接收用户明确选择的附件。</summary>
public partial class SshLogCaption : UserControl
{
    /// <summary>可完全离屏渲染的日志标题。</summary>
    public SshLogCaption() => InitializeComponent();
    private void OnAgent(object sender, RoutedEventArgs args)
    {
        if (DataContext is not SshContainerLogViewModel document) return;
        if (document.Assistant.Selection.Length > 0 && sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button; menu.IsOpen = true; return;
        }
        Open(false);
    }
    private void OnAttachSnapshot(object sender, RoutedEventArgs args) => Open(false);
    private void OnAttachSelection(object sender, RoutedEventArgs args) => Open(true);
    private void Open(bool selection)
    {
        if (DataContext is not SshContainerLogViewModel document || TryFindResource(typeof(IAgentWindowService)) is not IAgentWindowService windows) return;
        if (!document.Assistant.IsSourceClosed && document.Snapshot is not null && !document.Assistant.PrepareAttachment(selection)) return;
        var source = Window.GetWindow(this);
        windows.Open(new(document, document.Assistant, document.Title, document.Source, "file-text", () => new SshLogAssistantView(), () =>
        {
            if (source is null || document.Assistant.IsSourceClosed) return;
            if (source.WindowState == WindowState.Minimized) source.WindowState = WindowState.Normal;
            source.Show(); source.Activate();
        }, () => document.Assistant.IsSourceClosed));
    }
    private void OnMaximize(object sender, RoutedEventArgs args)
    {
        if (Window.GetWindow(this) is not { } window) return;
        if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window); else SystemCommands.MaximizeWindow(window);
    }
}
