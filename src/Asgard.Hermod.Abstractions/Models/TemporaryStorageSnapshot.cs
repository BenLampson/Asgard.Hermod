namespace Asgard.Hermod.Abstractions.Models;

/// <summary>一次扫描的安全快照，无法访问的项目独立报告，不虚构完整统计。</summary>
public sealed record TemporaryStorageSnapshot(long TotalBytes, int FileCount, long InUseBytes,
    int InUseFileCount, IReadOnlyList<TemporaryStorageUsage> Usage, int UnreadableItems = 0)
{
    /// <summary>能够清理的空间不包含租约占用和操作系统锁定的文件。</summary>
    public long CleanableBytes => TotalBytes - InUseBytes;
    /// <summary>可清理的文件数。</summary>
    public int CleanableFileCount => FileCount - InUseFileCount;
}
