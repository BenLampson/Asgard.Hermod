using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>限制弹出选择器高度，在锚点所在显示器内保留阴影空间。</summary>
internal static class PopupWorkArea
{
    /// <summary>Win32 返回设备像素，按锚点 DPI 转为 WPF 的逻辑尺寸。</summary>
    internal static double GetMaximumHeight(FrameworkElement anchor, double preferredHeight)
    {
        var position = anchor.PointToScreen(new Point(0, anchor.ActualHeight));
        var point = new NativePoint { X = (int)position.X, Y = (int)position.Y };
        nint monitor = WindowNativeMethods.MonitorFromPoint(point, 2);
        var information = new NativeMonitorInformation { Size = Marshal.SizeOf<NativeMonitorInformation>() };
        if (monitor == nint.Zero || !WindowNativeMethods.GetMonitorInfo(monitor, ref information))
            return preferredHeight;
        double scale = VisualTreeHelper.GetDpi(anchor).DpiScaleY;
        double workHeight = (information.WorkArea.Bottom - information.WorkArea.Top) / scale;
        return Math.Min(preferredHeight, Math.Max(0, workHeight - 32));
    }
}
