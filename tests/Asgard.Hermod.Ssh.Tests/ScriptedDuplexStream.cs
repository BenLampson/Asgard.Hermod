namespace Asgard.Hermod.Ssh.Tests;

/// <summary>代理边界替身将响应拆成单字节，主动暴露过读和分片问题。</summary>
internal sealed class ScriptedDuplexStream(byte[] response) : Stream
{
    private readonly MemoryStream _response = new(response);
    internal MemoryStream Requests { get; } = new();
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _response.Position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => _response.Read(buffer, offset, Math.Min(count, 1));
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(_response.Read(buffer.Span[..Math.Min(buffer.Length, 1)])); }
    public override void Write(byte[] buffer, int offset, int count) => Requests.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); Requests.Write(buffer.Span); return ValueTask.CompletedTask; }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) { _response.Dispose(); Requests.Dispose(); } base.Dispose(disposing); }
}
