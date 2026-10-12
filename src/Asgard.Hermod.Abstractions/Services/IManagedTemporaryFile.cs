namespace Asgard.Hermod.Abstractions.Services;

/// <summary>下载和预览期间持有独占租约，释放后自动清理本次请求。</summary>
public interface IManagedTemporaryFile : IDisposable
{
    /// <summary>已经创建隔离目录的安全文件路径。</summary>
    string Path { get; }
    /// <summary>外部打开成功后转为缓存；当前应用会话继续保护，退出时释放租约并保留正文。</summary>
    void RetainForExternalUse();
}
