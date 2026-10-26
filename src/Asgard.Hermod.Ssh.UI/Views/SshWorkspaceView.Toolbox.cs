using System.Windows.Controls.Primitives;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>工具箱按工具记忆宽高；基础设施默认均分，窄窗口上下排列并始终保留终端。</summary>
public partial class SshWorkspaceView
{
    private readonly Dictionary<string, double> _toolboxWidths = new(StringComparer.Ordinal) { ["files"] = 320, ["forward"] = 480, ["transfers"] = 360 };
    private readonly Dictionary<string, double> _toolboxHeights = new(StringComparer.Ordinal);
    private bool _updatingToolboxLayout;
    private bool _stackedToolbox;

    /// <summary>监听原生键盘和鼠标分隔器的实际请求尺寸。</summary>
    private void InitializeToolboxResizeTracking()
    {
        DependencyPropertyDescriptor.FromProperty(ColumnDefinition.WidthProperty, typeof(ColumnDefinition))?.AddValueChanged(ToolboxColumn, OnToolboxWidthChanged);
        DependencyPropertyDescriptor.FromProperty(RowDefinition.HeightProperty, typeof(RowDefinition))?.AddValueChanged(ToolboxRow, OnToolboxHeightChanged);
    }
    /// <summary>解除描述符监听，避免关闭后仍持有视图。</summary>
    private void StopToolboxResizeTracking()
    {
        DependencyPropertyDescriptor.FromProperty(ColumnDefinition.WidthProperty, typeof(ColumnDefinition))?.RemoveValueChanged(ToolboxColumn, OnToolboxWidthChanged);
        DependencyPropertyDescriptor.FromProperty(RowDefinition.HeightProperty, typeof(RowDefinition))?.RemoveValueChanged(ToolboxRow, OnToolboxHeightChanged);
    }
    private void OnToolboxWidthChanged(object? sender, EventArgs args)
    {
        if (!_updatingToolboxLayout && !_stackedToolbox && _model?.IsToolboxOpen == true && ToolboxColumn.Width.IsAbsolute)
            _toolboxWidths[_model.ActiveSection] = ToolboxColumn.Width.Value;
    }
    private void OnToolboxHeightChanged(object? sender, EventArgs args)
    {
        if (!_updatingToolboxLayout && _stackedToolbox && _model?.IsToolboxOpen == true && ToolboxRow.Height.IsAbsolute)
            _toolboxHeights[_model.ActiveSection] = ToolboxRow.Height.Value;
    }
    private void OnToolboxResizeCompleted(object sender, DragCompletedEventArgs args)
    {
        if (_model?.IsToolboxOpen != true) return;
        if (_stackedToolbox) _toolboxHeights[_model.ActiveSection] = ToolboxRow.ActualHeight;
        else _toolboxWidths[_model.ActiveSection] = ToolboxColumn.ActualWidth;
    }
    /// <summary>双击恢复默认均分；普通文件工具恢复其原来的宽度。</summary>
    private void OnToolboxReset(object sender, MouseButtonEventArgs args)
    {
        if (_model is null) return;
        _toolboxWidths.Remove(_model.ActiveSection); _toolboxHeights.Remove(_model.ActiveSection);
        UpdateToolboxLayout(); args.Handled = true;
    }
    /// <summary>窗口变化只限制实际布局，不覆盖用户记忆的尺寸。</summary>
    private void UpdateToolboxLayout()
    {
        if (ToolboxColumn is null || ToolboxGrid is null) return;
        bool open = _model?.IsToolboxOpen == true, management = _model?.IsManagementSection == true;
        bool stacked = open && management && ToolboxGrid.ActualWidth < 1050;
        _updatingToolboxLayout = true;
        try
        {
            _stackedToolbox = stacked;
            ToolboxRow.MinHeight = 0; ToolboxRow.MaxHeight = double.PositiveInfinity;
            ToolboxColumn.MinWidth = 0; ToolboxColumn.MaxWidth = double.PositiveInfinity;
            Grid.SetColumn(ToolboxPanel, 1); Grid.SetColumnSpan(ToolboxPanel, stacked ? 3 : 1);
            Grid.SetRow(ToolboxPanel, 0);
            Grid.SetColumn(ToolboxSplitter, stacked ? 1 : 2); Grid.SetColumnSpan(ToolboxSplitter, stacked ? 3 : 1);
            Grid.SetRow(ToolboxSplitter, stacked ? 1 : 0);
            Grid.SetColumn(SshBodyGrid, stacked ? 1 : 3); Grid.SetColumnSpan(SshBodyGrid, stacked ? 3 : 1);
            Grid.SetRow(SshBodyGrid, stacked ? 2 : 0);
            ToolboxSplitter.ResizeDirection = stacked ? GridResizeDirection.Rows : GridResizeDirection.Columns;
            ToolboxSplitter.Width = stacked ? double.NaN : management ? 8 : 6;
            ToolboxSplitter.Height = stacked ? 8 : double.NaN;
            ToolboxSplitter.VerticalAlignment = VerticalAlignment.Stretch;
            if (stacked)
            {
                double available = Math.Max(0, ToolboxGrid.ActualHeight - 8);
                double minimum = Math.Min(180, available / 2), maximum = Math.Max(minimum, available - 160);
                ToolboxRow.MinHeight = minimum; ToolboxRow.MaxHeight = maximum;
                ToolboxRow.Height = new(Math.Clamp(_toolboxHeights.GetValueOrDefault(_model!.ActiveSection, available / 2), minimum, maximum));
                ToolboxDividerRow.Height = new(8); TerminalRow.Height = new(1, GridUnitType.Star);
                ToolboxColumn.Width = new(0); ToolboxDividerColumn.Width = new(0);
            }
            else
            {
                ToolboxRow.Height = new(1, GridUnitType.Star); ToolboxDividerRow.Height = new(0); TerminalRow.Height = new(0);
                double divider = management ? 8 : 6;
                double available = Math.Max(0, ToolboxGrid.ActualWidth - 52 - divider);
                double minimum = management ? Math.Min(380, available / 2) : Math.Min(220, available / 2);
                double maximum = Math.Max(minimum, available - (management ? 440 : 360));
                ToolboxColumn.MinWidth = open ? minimum : 0; ToolboxColumn.MaxWidth = open ? maximum : double.PositiveInfinity;
                double initial = management ? available / 2 : _model?.ActiveSection == "forward" ? 480 : _model?.ActiveSection == "transfers" ? 360 : 320;
                ToolboxColumn.Width = new(open ? Math.Clamp(_toolboxWidths.GetValueOrDefault(_model!.ActiveSection, initial), minimum, maximum) : 0);
                ToolboxDividerColumn.Width = new(open ? divider : 0);
            }
        }
        finally { _updatingToolboxLayout = false; }
    }
}
