using System.Runtime.InteropServices;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>日志宽屏并排、窄屏临时覆盖列表，关闭后恢复相同筛选与选中项。</summary>
public partial class SshMonitorDockerView : UserControl
{
    private SshMonitorViewModel? _model;
    /// <summary>布局不打开新窗口，不向 PTY 发送日志命令。</summary>
    public SshMonitorDockerView()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (_model is not null) _model.PropertyChanged -= OnModelChanged;
            _model = args.NewValue as SshMonitorViewModel;
            if (_model is not null) _model.PropertyChanged += OnModelChanged;
            UpdateLogLayout();
        };
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(SshMonitorViewModel.IsContainerPaneOpen)) UpdateLogLayout(); }
    private void OnViewportChanged(object sender, SizeChangedEventArgs args) => UpdateLogLayout();
    private void UpdateLogLayout()
    {
        bool open = _model?.IsContainerPaneOpen == true, compact = ActualWidth < 1100;
        ListColumn.Width = new(open && compact ? 0 : 1, GridUnitType.Star);
        ContainerList.Visibility = open && compact ? Visibility.Collapsed : Visibility.Visible;
        LogsColumn.Width = open ? compact ? new(1, GridUnitType.Star) : new(Math.Min(560, ActualWidth * 0.42)) : new(0);
        LogDrawer.Margin = new(open && !compact ? 16 : 0, 0, 0, 0);
        bool stacked = ActualWidth < 650;
        Grid.SetRow(ContainerActions, stacked ? 1 : 0); Grid.SetColumn(ContainerActions, stacked ? 0 : 1);
        Grid.SetColumnSpan(ContainerIdentity, stacked ? 2 : 1); Grid.SetColumnSpan(ContainerActions, stacked ? 2 : 1);
        ContainerActions.Margin = new(0, stacked ? 8 : 0, 0, 0);
    }
    /// <summary>复制已展示日志，剪贴板暂时不可用时不影响连接。</summary>
    private void OnCopyLogs(object sender, RoutedEventArgs args)
    {
        if (_model is null || _model.LogText.Length == 0) return;
        try { Clipboard.SetText(_model.LogText); } catch (ExternalException) { }
    }
}
