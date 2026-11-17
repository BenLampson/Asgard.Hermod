namespace Asgard.Hermod.Mcp;

/// <summary>在 SDK 读取 JSON 行前限制消息长度，阻止异常客户端积累无界输入。</summary>
internal sealed class McpBoundedInput(Stream source) : Stream
{
    private const int MaximumLineBytes = 64 * 1024;
    private int _lineBytes;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Validate(source.Read(buffer, offset, count), buffer.AsSpan(offset));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Validate(count, buffer.Span);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Validate(int count, ReadOnlySpan<byte> buffer)
    {
        foreach (byte value in buffer[..count])
        {
            if (value is (byte)'\n' or (byte)'\r') _lineBytes = 0;
            else if (++_lineBytes > MaximumLineBytes) throw new IOException(Text.Get("McpRequestTooLarge"));
        }
        return count;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
