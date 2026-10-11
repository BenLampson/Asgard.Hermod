namespace Asgard.Hermod.WinTerminal.Local;

/// <summary>管理一个 ConPTY 的进程句柄、控制台句柄与同步通信管道。</summary>
internal sealed class ConPtyProcess : IDisposable
{
    private readonly SafeProcessHandle _process;
    private readonly Lock _consoleLock = new();
    private nint _console;

    /// <summary>写入控制台输入的宿主管道。</summary>
    internal FileStream Input { get; }

    /// <summary>读取控制台输出的宿主管道。</summary>
    internal FileStream Output { get; }

    /// <summary>直接由 CreateProcess 返回的进程编号。</summary>
    internal int ProcessId { get; }

    /// <summary>接管新建控制台、进程与宿主侧管道的所有权。</summary>
    private ConPtyProcess(nint console, NativeProcessInformation process, SafeFileHandle input, SafeFileHandle output)
    {
        _console = console;
        _process = new SafeProcessHandle(process.Process, true);
        ProcessId = (int)process.ProcessId;
        Input = new FileStream(input, FileAccess.Write, 1, false);
        Output = new FileStream(output, FileAccess.Read, 1, false);
    }

    /// <summary>建立同步管道，在控制台启动子进程后关闭子进程侧的宿主副本。</summary>
    internal static ConPtyProcess Start(TerminalSize size, string commandLine, string? directory)
    {
        CreatePipe(out var inputRead, out var inputWrite);
        using (inputRead)
        {
            SafeFileHandle? outputRead = null;
            nint console = 0;
            try
            {
                CreatePipe(out outputRead, out var outputWrite);
                using (outputWrite)
                {
                    Marshal.ThrowExceptionForHR(ConPtyNativeMethods.CreatePseudoConsole(new NativeCoordinate(size), inputRead, outputWrite, 0, out console));
                    var process = StartProcess(console, commandLine, directory);
                    ConPtyNativeMethods.CloseHandle(process.Thread);
                    return new ConPtyProcess(console, process, inputWrite, outputRead);
                }
            }
            catch
            {
                if (console != 0) ConPtyNativeMethods.ClosePseudoConsole(console);
                inputWrite.Dispose();
                outputRead?.Dispose();
                throw;
            }
        }
    }

    /// <summary>创建不继承的同步匿名管道；失败时释放已经产生的句柄。</summary>
    private static void CreatePipe(out SafeFileHandle read, out SafeFileHandle write)
    {
        if (ConPtyNativeMethods.CreatePipe(out read, out write, 0, 0))
            return;
        int error = Marshal.GetLastWin32Error();
        read.Dispose();
        write.Dispose();
        throw new Win32Exception(error, "Failed to create ConPTY pipe.");
    }

    /// <summary>建立并释放扩展启动属性，使用可写的 Unicode 命令行启动进程。</summary>
    private static NativeProcessInformation StartProcess(nint console, string commandLine, string? directory)
    {
        nuint bytes = 0;
        ConPtyNativeMethods.InitializeProcThreadAttributeList(0, 1, 0, ref bytes);
        if (bytes == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to size process attributes.");
        nint attributes = Marshal.AllocHGlobal((nint)bytes);
        bool initialized = false;
        try
        {
            if (!ConPtyNativeMethods.InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to initialize process attributes.");
            initialized = true;
            if (!ConPtyNativeMethods.UpdateProcThreadAttribute(attributes, 0, 0x00020016, console, (nuint)nint.Size, 0, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to attach ConPTY to process.");
            var startup = new NativeStartupInformationEx
            {
                StartupInfo = new NativeStartupInformation
                {
                    Size = Marshal.SizeOf<NativeStartupInformationEx>(),
                    // 明确使用空标准句柄，防止从 IDE 或测试运行器复制重定向管道。
                    // 子进程随后由 ConPTY 创建自己的控制台标准句柄。
                    Flags = 0x00000100,
                },
                AttributeList = attributes,
            };
            if (!ConPtyNativeMethods.CreateProcess(null, new StringBuilder(commandLine), 0, 0, false,
                0x00080000, 0, directory, ref startup, out var process))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to start ConPTY process.");
            return process;
        }
        finally
        {
            if (initialized) ConPtyNativeMethods.DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes);
        }
    }

    /// <summary>调整控制台网格，和控制台关闭操作串行访问句柄。</summary>
    internal void Resize(TerminalSize size)
    {
        lock (_consoleLock)
            if (_console != 0)
                Marshal.ThrowExceptionForHR(ConPtyNativeMethods.ResizePseudoConsole(_console, new NativeCoordinate(size)));
    }

    /// <summary>通过拥有的进程句柄等待退出，避免依据 PID 重新打开已经退出的进程。</summary>
    internal Task<int> WaitForExitAsync() => Task.Factory.StartNew(() =>
    {
        if (ConPtyNativeMethods.WaitForSingleObject(_process, uint.MaxValue) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to wait for ConPTY process.");
        if (!ConPtyNativeMethods.GetExitCodeProcess(_process, out uint code))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to read ConPTY exit code.");
        return unchecked((int)code);
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    /// <summary>取走句柄后关闭控制台；调用方在独立线程持续排空输出以避免死锁。</summary>
    internal void CloseConsole()
    {
        nint console;
        lock (_consoleLock)
        {
            console = _console;
            _console = 0;
        }
        if (console != 0) ConPtyNativeMethods.ClosePseudoConsole(console);
    }

    /// <summary>通信工作线程全部退出后释放宿主侧管道和进程句柄。</summary>
    public void Dispose()
    {
        Input.Dispose();
        Output.Dispose();
        _process.Dispose();
    }
}
