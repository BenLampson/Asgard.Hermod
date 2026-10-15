using System.Windows.Controls.Primitives;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>监测分屏记忆用户高度，窗口变小时始终保留可输入的终端区域。</summary>
public partial class SshWorkspaceView
{
    private double _monitorHeight = 580;

    /// <summary>显隐由模型控制，采样更新不能自动打开面板。</summary>
    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SshWorkspaceViewModel.IsMonitorOpen)) UpdateMonitorLayout();
    }

    /// <summary>窗口缩放只限制当前高度，不覆盖用户上次拖动的高度。</summary>
    private void OnMonitorViewportChanged(object sender, SizeChangedEventArgs args) => UpdateMonitorLayout();

    /// <summary>鼠标及原生键盘分隔器操作均保留在本视图生命周期内。</summary>
    private void OnMonitorResizeCompleted(object sender, DragCompletedEventArgs args) => _monitorHeight = MonitorRow.ActualHeight;

    /// <summary>窄小窗口仍保留至少一百二十像素终端，面板内部负责滚动。</summary>
    private void UpdateMonitorLayout()
    {
        if (MonitorRow is null || SshBodyGrid is null) return;
        double maximum = Math.Max(0, SshBodyGrid.ActualHeight - 44 - 120 - 8);
        MonitorRow.MinHeight = _model?.IsMonitorOpen == true ? Math.Min(180, maximum) : 0;
        MonitorRow.MaxHeight = maximum;
        MonitorRow.Height = new(_model?.IsMonitorOpen == true ? Math.Clamp(_monitorHeight, 0, maximum) : 0);
        SshBodyGrid.RowDefinitions[1].MinHeight = _model?.IsMonitorOpen == true ? Math.Min(120, SshBodyGrid.ActualHeight / 3) : 0;
    }
}
