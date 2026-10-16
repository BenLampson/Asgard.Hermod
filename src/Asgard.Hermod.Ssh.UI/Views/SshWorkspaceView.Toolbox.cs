using System.Windows.Controls.Primitives;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>实例工具箱独立记忆各工具宽度，收起后终端占回全部可用空间。</summary>
public partial class SshWorkspaceView
{
    private readonly Dictionary<string, double> _toolboxWidths = new(StringComparer.Ordinal) { ["files"] = 320, ["forward"] = 480, ["transfers"] = 360 };
    private bool _updatingToolboxLayout;
    private void InitializeToolboxResizeTracking() => DependencyPropertyDescriptor.FromProperty(ColumnDefinition.WidthProperty, typeof(ColumnDefinition))?.AddValueChanged(ToolboxColumn, OnToolboxWidthChanged);
    private void StopToolboxResizeTracking() => DependencyPropertyDescriptor.FromProperty(ColumnDefinition.WidthProperty, typeof(ColumnDefinition))?.RemoveValueChanged(ToolboxColumn, OnToolboxWidthChanged);
    private void OnToolboxWidthChanged(object? sender, EventArgs args)
    {
        if (!_updatingToolboxLayout && _model?.IsToolboxOpen == true && ToolboxColumn.Width.IsAbsolute) _toolboxWidths[_model.ActiveSection] = ToolboxColumn.Width.Value;
    }
    private void OnToolboxResizeCompleted(object sender, DragCompletedEventArgs args)
    {
        if (_model?.IsToolboxOpen == true) _toolboxWidths[_model.ActiveSection] = ToolboxColumn.ActualWidth;
    }
    private void UpdateToolboxLayout()
    {
        if (ToolboxColumn is null || ToolboxGrid is null) return;
        bool open = _model?.IsToolboxOpen == true;
        double maximum = Math.Max(200, ToolboxGrid.ActualWidth - 52 - 6 - 360);
        _updatingToolboxLayout = true;
        try
        {
            ToolboxColumn.MinWidth = open ? Math.Min(220, maximum) : 0; ToolboxColumn.MaxWidth = open ? maximum : double.PositiveInfinity;
            ToolboxColumn.Width = new(open ? Math.Clamp(_toolboxWidths.GetValueOrDefault(_model!.ActiveSection, 320), ToolboxColumn.MinWidth, maximum) : 0);
            ToolboxDividerColumn.Width = new(open ? 6 : 0);
        }
        finally { _updatingToolboxLayout = false; }
    }
}
