using System.Windows.Controls.Primitives;

namespace Asgard.Hermod.UI.Controls;

/// <summary>共享表格用 Shift 滚轮移动实际内部视口，不改变单元格编辑、选择或普通滚轮。</summary>
public static class WorkspaceGridScroll
{
    /// <summary>显式启用共享表格的水平滚轮。</summary>
    public static readonly DependencyProperty HorizontalWheelProperty = DependencyProperty.RegisterAttached("HorizontalWheel", typeof(bool), typeof(WorkspaceGridScroll), new PropertyMetadata(false, OnChanged));
    /// <summary>读取当前表格的行为开关。</summary>
    public static bool GetHorizontalWheel(DependencyObject target) => (bool)target.GetValue(HorizontalWheelProperty);
    /// <summary>公共样式注册一次，卸载或关闭不保留全局事件引用。</summary>
    public static void SetHorizontalWheel(DependencyObject target, bool value) => target.SetValue(HorizontalWheelProperty, value);
    private static void OnChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not DataGrid grid) return;
        if ((bool)args.NewValue) grid.PreviewMouseWheel += OnWheel;
        else grid.PreviewMouseWheel -= OnWheel;
    }
    private static void OnWheel(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || Keyboard.Modifiers != ModifierKeys.Shift || sender is not DataGrid grid) return;
        for (DependencyObject? source = args.OriginalSource as DependencyObject; source is not null && source != grid; source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source))
            if (source is TextBoxBase or PasswordBox or Selector or Popup) return;
        args.Handled = ScrollHorizontally(grid, args.Delta);
    }
    /// <summary>只操作 DataGrid 的主 ScrollViewer，并限制在真实可滚动范围。</summary>
    public static bool ScrollHorizontally(DataGrid grid, int delta)
    {
        if (grid.Template?.FindName("DG_ScrollViewer", grid) is not ScrollViewer viewer) return false;
        double next = Math.Clamp(viewer.HorizontalOffset - delta / 120d * 48, 0, viewer.ScrollableWidth);
        viewer.ScrollToHorizontalOffset(next); return true;
    }
}
