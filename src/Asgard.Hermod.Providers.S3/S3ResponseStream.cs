using Amazon.S3.Model;

namespace Asgard.Hermod.Providers.S3;

/// <summary>正文租约同时拥有 SDK 响应，预览提前结束时也能释放 HTTP 连接。</summary>
internal sealed class S3ResponseStream(GetObjectResponse response) : Stream
{
    private Stream Body => response.ResponseStream;
    /// <inheritdoc />
    public override bool CanRead => Body.CanRead;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => false;
    /// <inheritdoc />
    public override long Length => response.ContentLength;
    /// <inheritdoc />
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Body.Read(buffer, offset, count);
    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Body.ReadAsync(buffer, cancellationToken);
    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Body.ReadAsync(buffer, offset, count, cancellationToken);
    /// <inheritdoc />
    public override void Flush() { }
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <inheritdoc />
    protected override void Dispose(bool disposing) { if (disposing) response.Dispose(); base.Dispose(disposing); }
}
