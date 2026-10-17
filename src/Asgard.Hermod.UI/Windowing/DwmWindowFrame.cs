using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>让 Windows 统一绘制抗锯齿圆角和边框，避免多层 WPF 圆角与窗口区域互相裁切。</summary>
internal static class DwmWindowFrame
{
    /// <summary>自定义窗口使用系统正常圆角；最大化和贴靠时由系统自动移除。</summary>
    private const uint RoundCorners = 2;

    /// <summary>只使用四字节 DWM 属性，保留 HRESULT 以兼容不支持这些属性的系统。</summary>
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref uint value, int size);

    /// <summary>将不透明窗口的轮廓颜色和深色模式同步到桌面合成器；旧版系统保留原生直角。</summary>
    internal static void Apply(nint window, Color borderColor, bool dark)
    {
        if (window == nint.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        uint corners = RoundCorners;
        uint color = (uint)(borderColor.R | borderColor.G << 8 | borderColor.B << 16);
        uint darkMode = dark ? 1u : 0u;
        // 属性是外观提示，系统策略不接受时仍应保持窗口可以正常使用。
        _ = DwmSetWindowAttribute(window, 33, ref corners, sizeof(uint)); // DWMWA_WINDOW_CORNER_PREFERENCE
        _ = DwmSetWindowAttribute(window, 34, ref color, sizeof(uint)); // DWMWA_BORDER_COLOR
        _ = DwmSetWindowAttribute(window, 20, ref darkMode, sizeof(uint)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }
}
