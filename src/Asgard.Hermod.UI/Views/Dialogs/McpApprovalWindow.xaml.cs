using Asgard.Hermod.UI.Controls;
using Microsoft.Win32;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>独立显示 MCP 请求；关闭或拒绝不会放行请求。</summary>
public partial class McpApprovalWindow : HermodDialog
{
    private readonly McpApprovalViewModel _model;
    /// <summary>窗口只使用冻结的请求摘要，不读取连接配置。</summary>
    public McpApprovalWindow(McpApprovalViewModel model)
    {
        _model = model;
        InitializeComponent(); DataContext = model;
        ShowInTaskbar = true;
    }
    /// <summary>仅主动点击允许后为真。</summary>
    public bool Approved { get; private set; }
    private void OnApprove(object sender, RoutedEventArgs args) { if (_model.CanApprove) { Approved = true; Close(); } }
    private void OnDeny(object sender, RoutedEventArgs args) => Close();
    private void OnChooseFolder(object sender, RoutedEventArgs args)
    {
        var picker = new OpenFolderDialog { Title = Text.Get("McpStorageSelectTransferFolder") };
        if (picker.ShowDialog(this) == true) _model.DownloadFolder = picker.FolderName;
    }
}
