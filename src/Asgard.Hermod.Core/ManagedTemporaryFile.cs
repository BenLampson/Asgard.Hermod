namespace Asgard.Hermod.Core;

/// <summary>单次下载的租约，外部打开成功后所有权转交给管理服务直到应用退出。</summary>
internal sealed class ManagedTemporaryFile(LocalTemporaryFileStorage owner, string path, FileStream lease) : IManagedTemporaryFile
{
    private bool _retained;
    private bool _disposed;
    /// <inheritdoc />
    public string Path { get; } = path;
    /// <inheritdoc />
    public void RetainForExternalUse()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _retained = true;
        // 程序已经启动后，即使分类标记写入失败也不能删除它即将读取的正文。
        owner.Retain(this);
    }
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        owner.Release(this, _retained);
    }
    /// <summary>退出或清理时解除跨进程占用标记，重复调用安全。</summary>
    internal void ReleaseLease() => lease.Dispose();
}
