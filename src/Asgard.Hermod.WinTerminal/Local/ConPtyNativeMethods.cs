namespace Asgard.Hermod.WinTerminal.Local;

/// <summary>Windows ConPTY 与进程启动的最小 Win32 声明。</summary>
internal static class ConPtyNativeMethods
{
    /// <summary>建立同步匿名管道。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, nint attributes, uint size);

    /// <summary>创建伪控制台并关联输入、输出管道。</summary>
    [DllImport("kernel32.dll")]
    internal static extern int CreatePseudoConsole(NativeCoordinate size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint console);

    /// <summary>调整伪控制台尺寸。</summary>
    [DllImport("kernel32.dll")]
    internal static extern int ResizePseudoConsole(nint console, NativeCoordinate size);

    /// <summary>关闭伪控制台及关联的控制台进程。</summary>
    [DllImport("kernel32.dll")]
    internal static extern void ClosePseudoConsole(nint console);

    /// <summary>计算或初始化进程扩展属性列表。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nuint size);

    /// <summary>设置伪控制台进程属性；该属性的值直接传入 HPCON。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returnedSize);

    /// <summary>释放扩展属性列表中的系统资源。</summary>
    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(nint list);

    /// <summary>使用 Unicode 命令行与扩展启动信息创建进程。</summary>
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcess(string? application, StringBuilder commandLine, nint processAttributes,
        nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment,
        string? currentDirectory, ref NativeStartupInformationEx startupInfo, out NativeProcessInformation process);

    /// <summary>关闭原始 Win32 句柄。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    /// <summary>等待进程句柄进入退出状态。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);

    /// <summary>读取已经退出进程的退出码。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
}
