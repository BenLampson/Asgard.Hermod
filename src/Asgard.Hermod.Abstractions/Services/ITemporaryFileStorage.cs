namespace Asgard.Hermod.Abstractions.Services;

/// <summary>仅管理 Hermod 自己的临时文件，不接触配置数据库、用户下载目录或远端对象。</summary>
public interface ITemporaryFileStorage
{
    /// <summary>本机专用临时目录，用于只读展示和打开文件夹。</summary>
    string RootPath { get; }
    /// <summary>打开文件夹之前安全建立专用目录，并拒绝重解析路径。</summary>
    void EnsureDirectory();
    /// <summary>为单次打开或预览分配安全路径并持有跨进程租约。</summary>
    IManagedTemporaryFile CreateFile(string name);
    /// <summary>在后台扫描真实占用，同时识别使用中的请求。</summary>
    Task<Models.TemporaryStorageSnapshot> InspectAsync(CancellationToken cancellationToken = default);
    /// <summary>重新检查租约与文件锁后清理；自动维护只处理七天以前的请求。</summary>
    Task<Models.TemporaryStorageCleanupResult> CleanupAsync(bool expiredOnly = false,
        CancellationToken cancellationToken = default);
}
