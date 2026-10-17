namespace Asgard.Hermod.WinTerminal.Services;

/// <summary>拥有本地进程连接与终端缓冲区，保证释放顺序。</summary>
public sealed class LocalTerminalWorkspaceSession : ITerminalWorkspaceSession
{
    private readonly ConPtyConnection _connection;
    private Task? _disposeTask;

    /// <summary>接收已经启动的终端及 ConPTY 连接。</summary>
    public LocalTerminalWorkspaceSession(TerminalSession terminal, ConPtyConnection connection)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(connection);
        Terminal = terminal;
        _connection = connection;
    }

    /// <inheritdoc />
    public TerminalSession Terminal { get; }
    /// <inheritdoc />
    public string DisplayName => "Windows PowerShell";
    /// <inheritdoc />
    public Task<int> Completion => _connection.Completion;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposeTask ??= DisposeCoreAsync();
        return new ValueTask(_disposeTask);
    }

    /// <summary>排空管道和终止进程之后才释放屏幕状态。</summary>
    private async Task DisposeCoreAsync()
    {
        try { await _connection.DisposeAsync().ConfigureAwait(false); }
        finally { Terminal.Dispose(); }
    }
}
