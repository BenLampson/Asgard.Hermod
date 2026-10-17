using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>Win32 POINT，使用屏幕设备像素而非 WPF 逻辑单位。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    /// <summary>横坐标。</summary>
    internal int X;
    /// <summary>纵坐标。</summary>
    internal int Y;
}
