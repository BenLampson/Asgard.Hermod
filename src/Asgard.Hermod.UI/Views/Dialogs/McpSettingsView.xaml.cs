using Asgard.Hermod.UI.ViewModels;
using Microsoft.Win32;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>显示本机 MCP 状态、授权表单和范围列表。</summary>
public partial class McpSettingsView : UserControl
{
    /// <summary>加载支持主题和共享资源的设置布局。</summary>
    public McpSettingsView() => InitializeComponent();

    private void OnSelectTransferFolder(object sender, RoutedEventArgs args)
    {
        if (DataContext is not McpSettingsViewModel model || !model.ShowTransferRoot) return;
        var picker = new OpenFolderDialog { Title = Text.Get("McpStorageSelectTransferFolder") };
        if ((Window.GetWindow(this) is { } owner ? picker.ShowDialog(owner) : picker.ShowDialog()) == true)
            model.TransferRoot = picker.FolderName;
    }
}
