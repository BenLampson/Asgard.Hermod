using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>按窗口所在显示器的工作区和 DPI 限制首次显示的尺寸，避免标题栏或底部越界。</summary>
public static class WindowStartupPlacement
{
    /// <summary>在 HWND 已创建、首次显示前应用启动边界；仅调整正常状态且使用固定尺寸的窗口。</summary>
    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.WindowState != WindowState.Normal || window.SizeToContent != SizeToContent.Manual)
            return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
            return;
        var monitor = WindowNativeMethods.MonitorFromWindow(handle, 2); // MONITOR_DEFAULTTONEAREST
        var information = new NativeMonitorInformation { Size = Marshal.SizeOf<NativeMonitorInformation>() };
        if (monitor == nint.Zero || !WindowNativeMethods.GetMonitorInfo(monitor, ref information))
            return;
        var work = information.WorkArea;
        var pixels = new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
        var bounds = CalculateBounds(pixels, VisualTreeHelper.GetDpi(window), new Size(window.Width, window.Height));

        // 小屏或高缩放时，固定最小尺寸也可能超过工作区，必须先解除这个限制。
        window.MinWidth = Math.Min(window.MinWidth, bounds.Width);
        window.MinHeight = Math.Min(window.MinHeight, bounds.Height);
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
    }

    /// <summary>设备像素只除一次 DPI 转成 WPF 逻辑坐标；保留任务栏占位及副屏的负坐标。</summary>
    /// <param name="pixelWorkArea">目标显示器扣除任务栏后的设备像素区域。</param>
    /// <param name="dpi">目标窗口的实际 DPI 比例，而非主显示器或进程启动时的比例。</param>
    /// <param name="preferredSize">期望的 WPF 逻辑尺寸，空间足够时不放大或缩小。</param>
    /// <returns>在工作区内居中、四周预留至多二十四个逻辑像素的启动边界。</returns>
    public static Rect CalculateBounds(Rect pixelWorkArea, DpiScale dpi, Size preferredSize)
    {
        var work = new Rect(pixelWorkArea.X / dpi.DpiScaleX, pixelWorkArea.Y / dpi.DpiScaleY,
            pixelWorkArea.Width / dpi.DpiScaleX, pixelWorkArea.Height / dpi.DpiScaleY);
        double marginX = Math.Min(24, work.Width * 0.05);
        double marginY = Math.Min(24, work.Height * 0.05);
        double width = Math.Min(preferredSize.Width, work.Width - marginX * 2);
        double height = Math.Min(preferredSize.Height, work.Height - marginY * 2);
        return new Rect(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height);
    }
}
