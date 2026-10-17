using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>Win32 MINMAXINFO，最大化位置相对于窗口所在的显示器。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMinMaxInformation
{
    /// <summary>系统保留字段。</summary>
    internal NativePoint Reserved;
    /// <summary>最大化窗口的宽度和高度。</summary>
    internal NativePoint MaximumSize;
    /// <summary>最大化窗口在显示器内的起点。</summary>
    internal NativePoint MaximumPosition;
    /// <summary>系统与 WPF 决定的最小拖拽尺寸。</summary>
    internal NativePoint MinimumTrackSize;
    /// <summary>系统与 WPF 决定的最大拖拽尺寸。</summary>
    internal NativePoint MaximumTrackSize;
}
