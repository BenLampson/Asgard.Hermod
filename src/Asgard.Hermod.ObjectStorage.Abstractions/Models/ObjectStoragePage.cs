namespace Asgard.Hermod.ObjectStorage.Abstractions.Models;

/// <summary>按当前前缀返回的一批对象；游标仅适用于同一个列表查询。</summary>
public sealed record ObjectStoragePage(IReadOnlyList<ObjectStorageEntry> Entries, string? NextToken = null);
