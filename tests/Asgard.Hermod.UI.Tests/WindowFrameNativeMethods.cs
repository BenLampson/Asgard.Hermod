using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Tests;

/// <summary>读取真实窗口的区域和 DWM 属性，验证最终原生边框而非 XAML 配置。</summary>
internal static class WindowFrameNativeMethods
{
    /// <summary>没有应用自定义窗口区域时返回 ERROR（0），DWM 可以完整接管圆角。</summary>
    [DllImport("user32.dll")]
    internal static extern int GetWindowRgnBox(nint window, out NativeScreenRectangle bounds);

    /// <summary>读取四字节的 DWM 圆角偏好；颜色属性只支持设置，不用于查询。</summary>
    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint window, int attribute, out uint value, int size);

    /// <summary>测试窗口仅从桌面隐藏，仍由 DWM 合成，避免用透明度改变被测窗口类型。</summary>
    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint window, int attribute, ref uint value, int size);
}
