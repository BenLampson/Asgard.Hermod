namespace Asgard.Hermod.ObjectStorage.Abstractions.Models;

/// <summary>目录递归扫描的统计快照；文件夹不含所选目录，字节数仅包含当前可见文件版本。</summary>
public sealed record ObjectStorageFolderStatistics(long Files, long Folders, long Bytes, DateTimeOffset? LastModified = null);
