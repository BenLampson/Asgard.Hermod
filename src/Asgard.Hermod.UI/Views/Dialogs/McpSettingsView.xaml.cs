using Asgard.Hermod.UI.ViewModels;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>显示本机 MCP 状态与已授权范围，新增授权使用独立弹窗。</summary>
public partial class McpSettingsView : UserControl
{
    /// <summary>加载支持主题和共享资源的设置布局。</summary>
    public McpSettingsView() => InitializeComponent();

    private void OnOpenExclusion(object sender, RoutedEventArgs args)
    {
        if (DataContext is not McpSettingsViewModel { CanEditExclusions: true } model) return;
        model.BeginExclusionDraft();
        var dialog = new McpExclusionWindow(model);
        if (Window.GetWindow(this) is { } owner) dialog.Owner = owner;
        dialog.ShowDialog();
    }
}
