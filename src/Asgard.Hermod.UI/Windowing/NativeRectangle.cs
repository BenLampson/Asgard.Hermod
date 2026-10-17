using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>Win32 RECT，保留多显示器中可能为负的屏幕坐标。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeRectangle
{
    /// <summary>左边界。</summary>
    internal int Left;
    /// <summary>上边界。</summary>
    internal int Top;
    /// <summary>右边界。</summary>
    internal int Right;
    /// <summary>下边界。</summary>
    internal int Bottom;
}
