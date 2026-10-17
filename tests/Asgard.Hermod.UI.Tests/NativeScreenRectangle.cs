using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Tests;

/// <summary>Win32 RECT 的四个物理屏幕坐标，按顺序布局与原生 API 对齐。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeScreenRectangle
{
    /// <summary>矩形左边缘。</summary>
    internal int Left;
    /// <summary>矩形上边缘。</summary>
    internal int Top;
    /// <summary>矩形右边缘，不包含该列像素。</summary>
    internal int Right;
    /// <summary>矩形下边缘，不包含该行像素。</summary>
    internal int Bottom;

    /// <summary>转换为 WPF 矩形，但保留 API 返回的屏幕像素单位。</summary>
    internal readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
}
