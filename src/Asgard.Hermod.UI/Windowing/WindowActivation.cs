using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>恢复可见性并唤回最深的模态所属窗口，保留已输入内容。</summary>
public static class WindowActivation
{
    /// <summary>隐藏后恢复任务栏入口，最小化还原时保留之前的最大化状态。</summary>
    public static void Show(Window window)
    {
        window.ShowInTaskbar = true;
        if (!window.IsVisible) window.Show();
        nint handle = new WindowInteropHelper(window).Handle;
        if (window.WindowState == WindowState.Minimized) ShowWindow(handle, 9);
        Window target = FindModal() ?? window;
        while (target.OwnedWindows.Cast<Window>().LastOrDefault(child => child.IsVisible) is { } child)
            target = child;
        target.Activate();
        SetForegroundWindow(new WindowInteropHelper(target).Handle);
    }

    /// <summary>工作窗口不属于主窗体 Owner 链，线程模态循环中查找真正仍启用的对话框。</summary>
    public static Window? FindModal()
    {
        if (!ComponentDispatcher.IsThreadModal) return null;
        return Application.Current?.Windows.Cast<Window>().LastOrDefault(window => window.IsVisible
            && window.Owner is not null && IsWindowEnabled(new WindowInteropHelper(window).Handle));
    }

    /// <summary>恢复 HWND，遵循 Windows 保存的窗口位置。</summary>
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint handle, int command);
    /// <summary>请求前台权限；第二次启动通过 IPC 主动授予原进程权限。</summary>
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint handle);
    /// <summary>WPF IsEnabled 不体现 ShowDialog 对原生 HWND 的禁用。</summary>
    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(nint handle);
}
