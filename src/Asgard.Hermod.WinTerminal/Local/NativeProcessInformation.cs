namespace Asgard.Hermod.WinTerminal.Local;

/// <summary>CreateProcess 返回的进程与初始线程句柄。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeProcessInformation
{
    /// <summary>需要由宿主关闭的进程句柄。</summary>
    public nint Process;
    /// <summary>需要由宿主关闭的初始线程句柄。</summary>
    public nint Thread;
    /// <summary>进程编号。</summary>
    public uint ProcessId;
    /// <summary>初始线程编号。</summary>
    public uint ThreadId;
}
