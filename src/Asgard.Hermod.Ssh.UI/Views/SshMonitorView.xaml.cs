using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>只有用户打开详情并选择分区时才执行相应只读诊断。</summary>
public partial class SshMonitorView : UserControl
{
    /// <summary>要求宿主关闭监测详情。</summary>
    public event EventHandler? CloseRequested;
    /// <summary>加载监测展示，不自动创建额外连接。</summary>
    public SshMonitorView() { InitializeComponent(); IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); }; }
    private void OnClose(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnSectionChanged(object sender, SelectionChangedEventArgs args) { if (IsVisible) Refresh(); }
    private void Refresh() { if (DataContext is SshMonitorViewModel model) model.RefreshDetailsCommand.Execute(null); }
}
