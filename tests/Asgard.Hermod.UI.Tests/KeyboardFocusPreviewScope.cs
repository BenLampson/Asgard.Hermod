using System.Reflection;
using System.Windows.Documents;
using System.Windows.Input;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在不激活的测试 HWND 中执行 WPF 自身焦点绘制，不移动桌面键盘或鼠标焦点。</summary>
internal sealed class KeyboardFocusPreviewScope : IDisposable
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo Navigation = typeof(KeyboardNavigation).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly PropertyInfo InputDevice = typeof(InputManager).GetProperty(nameof(InputManager.MostRecentInputDevice))!;
    private static readonly PropertyInfo AlwaysShow = typeof(KeyboardNavigation).GetProperty("AlwaysShowFocusVisual", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo Show = typeof(KeyboardNavigation).GetMethod("ShowFocusVisual", PrivateInstance, [typeof(DependencyObject)])!;
    private static readonly MethodInfo Hide = typeof(KeyboardNavigation).GetMethod("HideFocusVisual", PrivateInstance)!;
    private readonly object _navigation = Navigation.GetValue(null)!;
    private readonly object? _previousDevice = InputDevice.GetValue(InputManager.Current);
    private readonly object? _previousAlwaysShow = AlwaysShow.GetValue(null);

    /// <summary>排除用户系统键盘提示偏好，分别验证键盘和鼠标输入来源。</summary>
    internal KeyboardFocusPreviewScope() => AlwaysShow.SetValue(null, false);

    /// <summary>使用 WPF 原生 FocusVisualStyle 绘制当前控件的键盘导航提示。</summary>
    internal void ShowKeyboard(Control target) => ShowFor(target, Keyboard.PrimaryDevice);

    /// <summary>同一控件恢复鼠标焦点时，应由 WPF 移除键盘提示而保留业务选择。</summary>
    internal void ShowMouseFocus(Control target) => ShowFor(target, Mouse.PrimaryDevice);

    /// <summary>只切换测试 Dispatcher 的输入来源；原生绘制器创建真实 Adorner。</summary>
    private void ShowFor(Control target, InputDevice device)
    {
        InputDevice.SetValue(InputManager.Current, device);
        Show.Invoke(_navigation, [target]);
    }

    /// <summary>读取控件真实焦点层，排除其它校验或窗口装饰。</summary>
    internal static Adorner[] Adorners(Control target) => AdornerLayer.GetAdornerLayer(target)?.GetAdorners(target) ?? [];

    /// <summary>结束时清理原生焦点层和输入来源，避免影响其它界面测试。</summary>
    public void Dispose()
    {
        Hide.Invoke(_navigation, null);
        InputDevice.SetValue(InputManager.Current, _previousDevice);
        AlwaysShow.SetValue(null, _previousAlwaysShow);
    }
}
