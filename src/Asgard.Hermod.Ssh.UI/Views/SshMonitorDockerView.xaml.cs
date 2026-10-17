using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>容器详情宽屏并排、窄屏覆盖列表；查看日志交由独立窗口。</summary>
public partial class SshMonitorDockerView : UserControl
{
    private SshMonitorViewModel? _model;
    /// <summary>监测布局只负责结构化列表和详情，不向 PTY 发送命令。</summary>
    public SshMonitorDockerView()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (_model is not null) _model.PropertyChanged -= OnModelChanged;
            _model = args.NewValue as SshMonitorViewModel;
            if (_model is not null) _model.PropertyChanged += OnModelChanged;
            UpdateDetailsLayout();
        };
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(SshMonitorViewModel.IsContainerPaneOpen)) UpdateDetailsLayout(); }
    private void OnViewportChanged(object sender, SizeChangedEventArgs args) => UpdateDetailsLayout();
    private void UpdateDetailsLayout()
    {
        bool open = _model?.IsContainerPaneOpen == true, compact = ActualWidth < 1100;
        ListColumn.Width = new(open && compact ? 0 : 1, GridUnitType.Star);
        ContainerList.Visibility = open && compact ? Visibility.Collapsed : Visibility.Visible;
        DetailsColumn.Width = open ? compact ? new(1, GridUnitType.Star) : new(Math.Min(560, ActualWidth * 0.42)) : new(0);
        ContainerDetailsDrawer.Margin = new(open && !compact ? 16 : 0, 0, 0, 0);
        bool stacked = ActualWidth < 650;
        Grid.SetRow(ContainerActions, stacked ? 1 : 0); Grid.SetColumn(ContainerActions, stacked ? 0 : 1);
        Grid.SetColumnSpan(ContainerIdentity, stacked ? 2 : 1); Grid.SetColumnSpan(ContainerActions, stacked ? 2 : 1);
        ContainerActions.Margin = new(0, stacked ? 8 : 0, 0, 0);
    }
}
