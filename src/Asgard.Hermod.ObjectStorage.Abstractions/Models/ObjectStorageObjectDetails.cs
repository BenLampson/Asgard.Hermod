namespace Asgard.Hermod.ObjectStorage.Abstractions.Models;

/// <summary>对象元数据及有大小上限的文本预览，文本只能作为内容展示。</summary>
public sealed record ObjectStorageObjectDetails(string Key, long Size, DateTimeOffset? LastModified,
    string StorageClass, string ContentType, string AccessControl,
    string? PreviewText = null, string? PreviewMessage = null);
