using Tmds.Ssh;

namespace Asgard.Hermod.Ssh;

/// <summary>拥有一次 SDK 连接、PTY 和可选代理桥；不拥有 UI 终端缓冲区。</summary>
internal sealed class SshConnection(SshClient client, RemoteProcess shell, SshProxyBridge? bridge,
    IReadOnlyList<TrustedHostKey> hostKeys) : ISshConnection
{
    private int _disposed;
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
        try { shell.Dispose(); }
        finally
        {
            client.Dispose();
            if (bridge is not null) await bridge.DisposeAsync().ConfigureAwait(false);
        }
    }
}
