namespace Asgard.Hermod.UI.Models;

/// <summary>一页对象列表中的文件或目录前缀，不包含任何访问凭据。</summary>
public sealed record ObjectStorageEntry(string Key, bool IsDirectory, long Size = 0,
    DateTimeOffset? LastModified = null, string StorageClass = "Standard");
