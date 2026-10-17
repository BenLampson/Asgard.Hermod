namespace Asgard.Hermod.WinTerminal.Local;

/// <summary>
/// 将 Windows ConPTY 的原始字节流接入会话；输入、输出及关闭操作在独立后台任务上执行。
/// </summary>
public sealed class ConPtyConnection : IAsyncDisposable
{
    private readonly TerminalSession _session;
    private readonly ConPtyProcess _process;
    private readonly Channel<ConPtyWriteRequest> _requests = Channel.CreateUnbounded<ConPtyWriteRequest>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false,
    });
    private readonly Task _readTask;
    private readonly Task _writeTask;
    private readonly Lock _closeLock = new();
    private Task? _closeTask;
    private Exception? _failure;
    private int _closing;

    /// <summary>进程退出且最后输出处理完成后的退出码；后台通信故障会使任务失败。</summary>
    public Task<int> Completion { get; }

    /// <summary>获取系统创建的子进程编号。</summary>
    public int ProcessId => _process.ProcessId;

    /// <summary>创建连接并订阅会话的输入与尺寸事件。</summary>
    private ConPtyConnection(TerminalSession session, ConPtyProcess process)
    {
        _session = session;
        _process = process;
        session.InputReceived += OnInputReceived;
        session.SizeChanged += OnSizeChanged;
        _readTask = StartWorker(ReadOutput);
        _writeTask = StartWorker(WriteInput);
        Completion = ObserveExitAsync();
    }

    /// <summary>
    /// 启动指定本地命令行，例如 cmd.exe 或 powershell.exe -NoLogo；不会开启额外控制台窗口。
    /// </summary>
    public static ConPtyConnection Start(TerminalSession session, string commandLine, string? workingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        ObjectDisposedException.ThrowIf(session.IsDisposed, session);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(commandLine.Length, 32766);
        if (commandLine.Contains('\0'))
            throw new ArgumentException("Command line cannot contain a null character.", nameof(commandLine));
        lock (session.Buffer.SyncRoot)
        {
            ObjectDisposedException.ThrowIf(session.IsDisposed, session);
            var size = new TerminalSize(session.Buffer.Columns, session.Buffer.Rows);
            return new ConPtyConnection(session, ConPtyProcess.Start(size, commandLine, workingDirectory));
        }
    }

    /// <summary>将会话输入排队，避免 UI 与解析器回调直接阻塞在同步管道写入。</summary>
    private void OnInputReceived(object? sender, ReadOnlyMemory<byte> data)
    {
        if (Volatile.Read(ref _closing) == 0)
            _requests.Writer.TryWrite(new ConPtyWriteRequest(data.ToArray(), null));
    }

    /// <summary>按输入顺序排队转发 PTY 尺寸变化。</summary>
    private void OnSizeChanged(object? sender, TerminalSize size)
    {
        if (Volatile.Read(ref _closing) == 0)
            _requests.Writer.TryWrite(new ConPtyWriteRequest(null, size));
    }

    /// <summary>持续排空输出，关闭 ConPTY 时仍读取系统发送的最后一帧。</summary>
    private void ReadOutput()
    {
        byte[] bytes = new byte[8192];
        try
        {
            int count;
            while ((count = _process.Output.Read(bytes)) > 0)
            {
                if (_session.IsDisposed)
                    continue;
                try { _session.FeedOutput(bytes.AsSpan(0, count)); }
                catch (ObjectDisposedException) when (_session.IsDisposed) { }
            }
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
        finally
        {
            _ = CloseConsoleAsync();
        }
    }

    /// <summary>使用独立工作线程处理输入和调整尺寸，避免与输出管道互相等待。</summary>
    private void WriteInput()
    {
        try
        {
            while (_requests.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (_requests.Reader.TryRead(out var request))
                {
                    if (Volatile.Read(ref _closing) != 0)
                        continue;
                    if (request.Size is { } size)
                        _process.Resize(size);
                    else if (request.Input is { } input)
                    {
                        _process.Input.Write(input);
                        _process.Input.Flush();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            _ = CloseConsoleAsync();
        }
    }

    /// <summary>记录非关闭期间的故障；正常断开后的管道异常视为退出的一部分。</summary>
    private void RecordFailure(Exception exception)
    {
        if (Volatile.Read(ref _closing) == 0)
            Interlocked.CompareExchange(ref _failure, exception, null);
    }

    /// <summary>等待子进程并收尾两个通信工作线程。</summary>
    private async Task<int> ObserveExitAsync()
    {
        try
        {
            int exitCode = await _process.WaitForExitAsync().ConfigureAwait(false);
            await CloseConsoleAsync().ConfigureAwait(false);
            await Task.WhenAll(_readTask, _writeTask).ConfigureAwait(false);
            if (_failure is { } failure)
                throw new IOException("ConPTY communication failed.", failure);
            return exitCode;
        }
        finally
        {
            try
            {
                await CloseConsoleAsync().ConfigureAwait(false);
                await Task.WhenAll(_readTask, _writeTask).ConfigureAwait(false);
            }
            finally
            {
                _session.InputReceived -= OnInputReceived;
                _session.SizeChanged -= OnSizeChanged;
                _process.Dispose();
            }
        }
    }

    /// <summary>关闭必须在后台执行，输出工作线程在关闭结束前保持排空管道。</summary>
    private Task CloseConsoleAsync()
    {
        lock (_closeLock)
        {
            if (_closeTask is not null)
                return _closeTask;
            Interlocked.Exchange(ref _closing, 1);
            _requests.Writer.TryComplete();
            _closeTask = StartWorker(_process.CloseConsole);
            return _closeTask;
        }
    }

    /// <summary>为同步 Win32 管道建立独立工作线程，防止占满线程池。</summary>
    private static Task StartWorker(Action action) => Task.Factory.StartNew(action, CancellationToken.None,
        TaskCreationOptions.LongRunning, TaskScheduler.Default);

    /// <summary>终止本连接的控制台进程并等待 I/O 收尾；会话由宿主另行释放。</summary>
    public async ValueTask DisposeAsync()
    {
        await CloseConsoleAsync().ConfigureAwait(false);
        await Completion.ConfigureAwait(false);
    }
}
