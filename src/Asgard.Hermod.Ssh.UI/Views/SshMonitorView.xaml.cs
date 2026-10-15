using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>视图控制按需读取与紧凑导航，不改变连接或终端焦点。</summary>
public partial class SshMonitorView : UserControl
{
    /// <summary>通知宿主收起分屏。</summary>
    public event EventHandler? CloseRequested;
    /// <summary>显隐只影响额外详情读取，采样历史由模型拥有。</summary>
    public SshMonitorView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => { if (DataContext is SshMonitorViewModel model) model.IsPanelVisible = IsVisible; };
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is SshMonitorViewModel old) old.IsPanelVisible = false;
            if (args.NewValue is SshMonitorViewModel current) current.IsPanelVisible = IsVisible;
        };
    }
    private void OnClose(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);
    /// <summary>小宽度保留图标动作和原生可横向滚动的页签。</summary>
    private void OnViewportChanged(object sender, SizeChangedEventArgs args)
    {
        MonitorHeading.Visibility = ActualWidth >= 1100 ? Visibility.Visible : Visibility.Collapsed;
        SamplingHint.Visibility = ActualWidth >= 1300 ? Visibility.Visible : Visibility.Collapsed;
        PauseLabel.Visibility = RefreshLabel.Visibility = CollapseLabel.Visibility = ActualWidth >= 850 ? Visibility.Visible : Visibility.Collapsed;
    }
}
