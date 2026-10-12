namespace Asgard.Hermod.Providers.AliyunOss;

/// <summary>不复制大文件的可寻址分片流；释放分片不会关闭由应用服务拥有的源文件。</summary>
internal sealed class ObjectStorageSliceStream : Stream
{
    /// <summary>源文件在一次上传中按分片顺序读取。</summary>
    private readonly Stream _source;
    /// <summary>分片在源文件内的起始位置。</summary>
    private readonly long _offset;
    /// <summary>分片的固定长度。</summary>
    private readonly long _length;

    /// <summary>分片不拥有源流，并要求源文件支持随机定位。</summary>
    public ObjectStorageSliceStream(Stream source, long offset, long length)
    {
        if (!source.CanRead || !source.CanSeek) throw new ArgumentException(Text.Get("TheUploadSourceStreamMustBeReadableAndSeekable"), nameof(source));
        _source = source;
        _offset = offset;
        _length = length;
        source.Position = offset;
    }

    /// <inheritdoc />
    public override bool CanRead => true;
    /// <inheritdoc />
    public override bool CanSeek => true;
    /// <inheritdoc />
    public override bool CanWrite => false;
    /// <inheritdoc />
    public override long Length => _length;
    /// <inheritdoc />
    public override long Position
    {
        get => _source.Position - _offset;
        set
        {
            if (value < 0 || value > _length) throw new ArgumentOutOfRangeException(nameof(value));
            _source.Position = _offset + value;
        }
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        _source.Read(buffer, offset, (int)Math.Min(count, _length - Position));
    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => _source.Read(buffer[..(int)Math.Min(buffer.Length, _length - Position)]);
    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _length - Position)], cancellationToken);
    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _source.ReadAsync(buffer, offset, (int)Math.Min(count, _length - Position), cancellationToken);
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        return Position;
    }

    /// <inheritdoc />
    public override void Flush() { }
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
