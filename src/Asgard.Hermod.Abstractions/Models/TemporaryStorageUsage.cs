namespace Asgard.Hermod.Abstractions.Models;

/// <summary>单个临时文件分类的真实磁盘占用。</summary>
public sealed record TemporaryStorageUsage(TemporaryFileCategory Category, long Bytes, int FileCount);
