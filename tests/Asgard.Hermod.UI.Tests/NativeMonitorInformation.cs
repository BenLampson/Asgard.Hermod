using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Tests;

/// <summary>Win32 MONITORINFO；尺寸字段必须在调用 GetMonitorInfo 前初始化。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMonitorInformation
{
    /// <summary>结构体总字节数，必须包含两个 RECT 和标志字段。</summary>
    internal uint Size;
    /// <summary>显示器完整边界。</summary>
    internal NativeScreenRectangle Monitor;
    /// <summary>排除任务栏等保留区域后的显示器工作区。</summary>
    internal NativeScreenRectangle WorkArea;
    /// <summary>显示器标志，例如主显示器标识。</summary>
    internal uint Flags;
}
