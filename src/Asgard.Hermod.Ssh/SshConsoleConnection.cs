using Tmds.Ssh;

namespace Asgard.Hermod.Ssh;

/// <summary>只拥有一个远端进程，禁止释放所属客户端或其他通道。</summary>
internal sealed class SshConsoleConnection(RemoteProcess shell) : ISshConsoleConnection
{
    private int _disposed;
    /// <inheritdoc />
    public async ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken)
        => (await shell.ReadAsync(output, output, cancellationToken).ConfigureAwait(false)).BytesRead;
    /// <inheritdoc />
    public ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken) => shell.WriteAsync(input, cancellationToken);
    /// <inheritdoc />
    public void Resize(int columns, int rows) => shell.SetTerminalSize(new(columns, rows));
    /// <inheritdoc />
    public ValueTask<int> GetExitCodeAsync(CancellationToken cancellationToken) => shell.GetExitCodeAsync(cancellationToken);
    /// <inheritdoc />
    public ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) == 0) shell.Dispose(); return ValueTask.CompletedTask; }
}
