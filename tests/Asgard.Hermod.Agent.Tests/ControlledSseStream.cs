using System.Text;
using System.Threading.Channels;

namespace Asgard.Hermod.Agent.Tests;

/// <summary>每次只释放测试明确写入的字节；空队列模拟尚未结束的真实连接。</summary>
internal sealed class ControlledSseStream : Stream
{
    private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
    private ReadOnlyMemory<byte> _remaining;
    internal bool WasDisposed { get; private set; }
    internal TaskCompletionSource Waiting { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void Push(string data, bool splitBytes = false)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(data);
        if (splitBytes) foreach (byte value in bytes) _chunks.Writer.TryWrite([value]);
        else _chunks.Writer.TryWrite(bytes);
    }
    internal void Finish() => _chunks.Writer.Complete();
    internal void ResetWaiting() => Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_remaining.IsEmpty)
        {
            if (!_chunks.Reader.TryRead(out var chunk))
            {
                Waiting.TrySetResult();
                if (!await _chunks.Reader.WaitToReadAsync(cancellationToken)) return 0;
                continue;
            }
            _remaining = chunk;
        }
        int length = Math.Min(buffer.Length, _remaining.Length); _remaining[..length].CopyTo(buffer); _remaining = _remaining[length..]; return length;
    }
    protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
