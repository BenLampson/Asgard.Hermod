using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Tests;

/// <summary>测试专用的显示器工作区查询，不修改桌面设置或窗口样式。</summary>
internal static class WindowBoundsNativeMethods
{
    /// <summary>窗口没有交集时返回距离窗口最近的显示器。</summary>
    private const uint DefaultToNearest = 2;

    /// <summary>查找真实 HWND 当前所在的显示器。</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    /// <summary>读取 MONITORINFO 布局及工作区，布尔返回值按 Win32 BOOL 封送。</summary>
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInformation information);

    /// <summary>获取该窗口显示器的工作区，查询失败会保留原生错误码。</summary>
    internal static Rect GetWorkArea(IntPtr window)
    {
        var monitor = MonitorFromWindow(window, DefaultToNearest);
        Assert.NotEqual(IntPtr.Zero, monitor);
        var information = new NativeMonitorInformation
        {
            Size = (uint)Marshal.SizeOf<NativeMonitorInformation>(),
        };
        if (!GetMonitorInfo(monitor, ref information))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return information.WorkArea.ToRect();
    }
}
