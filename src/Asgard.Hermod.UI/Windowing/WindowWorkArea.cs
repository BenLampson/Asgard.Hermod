using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>修正自定义 WindowChrome 的最大化尺寸，让完整客户区处于可见工作区内。</summary>
internal static class WindowWorkArea
{
    /// <summary>仅替换最大化边界，保留 WPF 对正常拖拽尺寸及还原位置的管理。</summary>
    internal static void ApplyMaximizedBounds(nint window, nint messageData)
    {
        if (messageData == nint.Zero)
            return;
        nint monitor = WindowNativeMethods.MonitorFromWindow(window, 2); // MONITOR_DEFAULTTONEAREST
        if (monitor == nint.Zero)
            return;
        var information = new NativeMonitorInformation { Size = Marshal.SizeOf<NativeMonitorInformation>() };
        if (!WindowNativeMethods.GetMonitorInfo(monitor, ref information))
            return;
        var limits = Marshal.PtrToStructure<NativeMinMaxInformation>(messageData);
        var workArea = information.WorkArea;
        // 两个 Win32 接口都使用设备像素，不能再乘一次 DPI；坐标转换留给 WPF。
        limits.MaximumPosition.X = workArea.Left - information.Monitor.Left;
        limits.MaximumPosition.Y = workArea.Top - information.Monitor.Top;
        limits.MaximumSize.X = workArea.Right - workArea.Left;
        limits.MaximumSize.Y = workArea.Bottom - workArea.Top;
        Marshal.StructureToPtr(limits, messageData, false);
    }
}
