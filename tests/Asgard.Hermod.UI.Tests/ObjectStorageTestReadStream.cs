namespace Asgard.Hermod.UI.Tests;

/// <summary>记录读取量和释放行为，支持在首个数据块后模拟用户取消。</summary>
public sealed class ObjectStorageTestReadStream(byte[] bytes, CancellationTokenSource? cancelAfterRead = null) : Stream
{
    /// <summary>可重复的内存源，用于限定读取字节数。</summary>
    private readonly MemoryStream _source = new(bytes);
    /// <summary>已读取的实际正文长度。</summary>
    public long BytesRead { get; private set; }
    /// <summary>正文是否在失败或取消后释放。</summary>
    public bool IsDisposed { get; private set; }
    /// <summary>模拟流关闭失败，验证下载提交边界。</summary>
    public bool ThrowOnDispose { get; set; }
    /// <inheritdoc />
    public override bool CanRead => true;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => false;
    /// <inheritdoc />
    public override long Length => _source.Length;
    /// <inheritdoc />
    public override long Position { get => _source.Position; set => throw new NotSupportedException(); }
    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => _source.Read(buffer, offset, count);
    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await _source.ReadAsync(buffer, cancellationToken);
        BytesRead += read;
        if (read > 0) cancelAfterRead?.Cancel();
        return read;
    }
    /// <inheritdoc />
    public override void Flush() { }
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        if (disposing) _source.Dispose();
        base.Dispose(disposing);
        if (disposing && ThrowOnDispose) throw new IOException("private-resource-token raw stream cleanup");
    }
}
