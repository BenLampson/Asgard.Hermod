using Tmds.Ssh;

namespace Asgard.Hermod.Ssh;

/// <summary>拥有一次 SDK 连接、PTY 和可选代理桥；不拥有 UI 终端缓冲区。</summary>
internal sealed partial class SshConnection(SshClient client, RemoteProcess shell, SshProxyBridge? bridge,
    IReadOnlyList<TrustedHostKey> hostKeys) : ISshWorkspaceConnection
{
    private int _disposed;
    private readonly SshConsoleConnection _initialConsole = new(shell);
    /// <inheritdoc />
    public ISshConsoleConnection InitialConsole => _initialConsole;
    /// <inheritdoc />
    // SDK 在释放后不允许再次读取属性；提前保存令牌供迟到的 Shell 完成通知使用。
    public CancellationToken Disconnected { get; } = client.Disconnected;
    /// <inheritdoc />
    public async Task<ISshConsoleConnection> OpenConsoleAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var process = await client.ExecuteShellAsync(new ExecuteOptions { AllocateTerminal = true, TerminalType = "xterm-256color", TerminalSize = new(columns, rows) }, cancellationToken).ConfigureAwait(false);
        if (Volatile.Read(ref _disposed) != 0 || cancellationToken.IsCancellationRequested)
        {
            process.Dispose(); cancellationToken.ThrowIfCancellationRequested(); throw new ObjectDisposedException(nameof(SshConnection));
        }
        return new SshConsoleConnection(process);
    }
    /// <inheritdoc />
    public IReadOnlyList<TrustedHostKey> HostKeys { get; } = hostKeys;
    /// <inheritdoc />
    public async ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken)
    {
        var result = await shell.ReadAsync(output, output, cancellationToken).ConfigureAwait(false);
        return result.BytesRead;
    }
    /// <inheritdoc />
    public ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken) => shell.WriteAsync(input, cancellationToken);
    /// <inheritdoc />
    public void Resize(int columns, int rows) => shell.SetTerminalSize(new(columns, rows));
    /// <inheritdoc />
    public ValueTask<int> GetExitCodeAsync(CancellationToken cancellationToken) => shell.GetExitCodeAsync(cancellationToken);
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { await _initialConsole.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            client.Dispose();
            if (bridge is not null) await bridge.DisposeAsync().ConfigureAwait(false);
        }
    }
}
