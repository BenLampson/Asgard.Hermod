namespace Asgard.Hermod.UI.Services;

/// <summary>存储设置的确认和文件夹打开边界，清理本身仍由临时存储服务执行。</summary>
public interface IStorageSettingsDialogs
{
    /// <summary>明确展示预计空间后取得确认；取消不能调用清理服务。</summary>
    Task<bool> ConfirmCleanupAsync(long estimatedBytes, int fileCount, CancellationToken cancellationToken = default);
    /// <summary>只在用户点击时打开专用存储目录。</summary>
    void OpenFolder(string path);
}
