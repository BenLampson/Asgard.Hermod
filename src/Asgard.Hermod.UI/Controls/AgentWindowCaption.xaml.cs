namespace Asgard.Hermod.UI.Controls;

/// <summary>可离屏验证的紧凑助手标题栏。</summary>
public partial class AgentWindowCaption : UserControl
{
    /// <summary>加载 44 像素标题栏。</summary>
    public AgentWindowCaption() => InitializeComponent();
    private void OnMaximize(object sender, RoutedEventArgs args)
    {
        if (Window.GetWindow(this) is not { } window) return;
        if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
        else SystemCommands.MaximizeWindow(window);
    }
}
