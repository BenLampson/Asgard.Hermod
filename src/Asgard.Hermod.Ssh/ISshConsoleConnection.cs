namespace Asgard.Hermod.Ssh;

/// <summary>一个独立 PTY 的所有权边界；释放它只关闭该 Shell，不关闭共享 SSH 传输。</summary>
public interface ISshConsoleConnection : IAsyncDisposable
{
    /// <summary>按原始字节读取；零字节表示此 Shell 已结束。</summary>
    ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken);
    /// <summary>发送到此 Shell，绝不重放。</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken);
    /// <summary>只调整此 PTY 的网格。</summary>
    void Resize(int columns, int rows);
    /// <summary>读取此 Shell 的退出码。</summary>
    ValueTask<int> GetExitCodeAsync(CancellationToken cancellationToken);
}
