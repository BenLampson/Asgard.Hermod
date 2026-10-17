using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>Win32 MONITORINFO，提供显示器范围以及扣除任务栏后的工作区。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMonitorInformation
{
    /// <summary>结构体的字节大小。</summary>
    internal int Size;
    /// <summary>显示器的完整屏幕范围。</summary>
    internal NativeRectangle Monitor;
    /// <summary>系统允许窗口使用的工作区。</summary>
    internal NativeRectangle WorkArea;
    /// <summary>系统显示器标记。</summary>
    internal uint Flags;
}
