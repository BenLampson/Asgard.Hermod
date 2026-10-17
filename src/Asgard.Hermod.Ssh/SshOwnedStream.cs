using Tmds.Ssh;

namespace Asgard.Hermod.Ssh;

/// <summary>将转发流和 SSH 客户端组成一个所有权单元。</summary>
internal sealed class SshOwnedStream(Stream stream, SshClient client) : Stream
{
    public override bool CanRead => stream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => stream.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => stream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => stream.ReadAsync(buffer, cancellationToken);
    public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => stream.WriteAsync(buffer, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) { stream.Dispose(); client.Dispose(); } base.Dispose(disposing); }
}
