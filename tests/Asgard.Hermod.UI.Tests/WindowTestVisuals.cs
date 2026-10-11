using System.Windows.Input;

namespace Asgard.Hermod.UI.Tests;

/// <summary>读取真实窗口的显示树和屏幕边界，不使用静态 XAML 尺寸代替布局。</summary>
internal static class WindowTestVisuals
{
    /// <summary>遍历已实例化的显示树，包含模板创建的控件。</summary>
    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T matching)
                yield return matching;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    /// <summary>获取通用模板中的整窗边框，用于比较原生工作区。</summary>
    internal static Border Frame(HermodWindow window) =>
        Assert.IsType<Border>(window.Template.FindName("PART_WindowFrame", window));

    /// <summary>获取通用模板中的标题槽，验证主窗与子窗的实际标题高度。</summary>
    internal static FrameworkElement Caption(HermodWindow window) =>
        Assert.IsAssignableFrom<FrameworkElement>(window.Template.FindName("PART_Caption", window));

    /// <summary>获取通用模板中的底部槽，验证子窗动作区不会挤入主体。</summary>
    internal static FrameworkElement Footer(HermodWindow window) =>
        Assert.IsAssignableFrom<FrameworkElement>(window.Template.FindName("PART_Footer", window));

    /// <summary>将布局尺寸换算为物理像素，与 HWND 和显示器 API 使用相同单位。</summary>
    internal static Rect ScreenBounds(FrameworkElement element)
    {
        Point origin = element.PointToScreen(new Point());
        var dpi = VisualTreeHelper.GetDpi(element);
        return new Rect(origin.X, origin.Y, element.ActualWidth * dpi.DpiScaleX,
            element.ActualHeight * dpi.DpiScaleY);
    }

    /// <summary>每条边允许一像素舍入误差，避免接受隐藏的 resize frame 偏移。</summary>
    internal static void AssertBounds(Rect expected, Rect actual, string reason)
    {
        Assert.True(Math.Abs(actual.Left - expected.Left) <= 1
            && Math.Abs(actual.Top - expected.Top) <= 1
            && Math.Abs(actual.Right - expected.Right) <= 1
            && Math.Abs(actual.Bottom - expected.Bottom) <= 1,
            $"{reason} Expected={expected}; Actual={actual}");
    }

    /// <summary>等待原生尺寸、绑定和布局完成，未激活的测试窗口也要刷新命令状态。</summary>
    internal static async Task UpdateLayoutAsync(Window window)
    {
        await WpfTestFixture.DrainDispatcherAsync();
        window.UpdateLayout();
        // 不依赖桌面鼠标移动触发 RequerySuggested，显式查询已建立绑定的按钮命令。
        CommandManager.InvalidateRequerySuggested();
        await WpfTestFixture.DrainDispatcherAsync();
    }

    /// <summary>沿实际按钮的命令目标执行用户动作，不直接调用被测窗口方法。</summary>
    internal static void Invoke(Button button)
    {
        Assert.True(button.IsEnabled,
            $"Button={button.Name}; Command={button.Command}; Target={button.CommandTarget}; "
            + $"Window={Window.GetWindow(button)?.WindowState}; ResizeMode={Window.GetWindow(button)?.ResizeMode}");
        var command = Assert.IsAssignableFrom<ICommand>(button.Command);
        if (command is RoutedCommand routed)
        {
            var target = button.CommandTarget ?? button;
            Assert.True(routed.CanExecute(button.CommandParameter, target));
            routed.Execute(button.CommandParameter, target);
        }
        else
        {
            Assert.True(command.CanExecute(button.CommandParameter));
            command.Execute(button.CommandParameter);
        }
    }
}
