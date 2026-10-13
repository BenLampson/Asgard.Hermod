namespace Asgard.Hermod.Ssh;

/// <summary>交互终端的协议边界，UI 只传输字节与网格尺寸。</summary>
public interface ISshConnection : IAsyncDisposable
{
    /// <summary>本次连接核验过的公开主机身份。</summary>
    IReadOnlyList<TrustedHostKey> HostKeys { get; }
    /// <summary>按原始字节读取远端输出，零字节表示会话结束。</summary>
    ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken);
    /// <summary>串行发送终端输入，不能自动重放。</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken);
    /// <summary>将字符网格变化通知远端 PTY。</summary>
    void Resize(int columns, int rows);
    /// <summary>输出读取结束后获取远程退出码。</summary>
    ValueTask<int> GetExitCodeAsync(CancellationToken cancellationToken);
}
