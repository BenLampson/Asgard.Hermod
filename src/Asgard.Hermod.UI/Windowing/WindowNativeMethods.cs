using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>窗口尺寸计算使用的最小 Win32 接口，不承担 UI 状态。</summary>
internal static class WindowNativeMethods
{
    /// <summary>按弹出菜单锚点选择显示器，支持窗口横跨不同工作区。</summary>
    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(NativePoint point, uint flags);

    /// <summary>选择与窗口相交最多的显示器，窗口在屏幕之外时选择最近的一台。</summary>
    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint window, uint flags);

    /// <summary>读取指定显示器当前的屏幕范围与工作区。</summary>
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref NativeMonitorInformation information);
}
