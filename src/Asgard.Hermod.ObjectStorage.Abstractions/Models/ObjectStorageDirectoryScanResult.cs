namespace Asgard.Hermod.ObjectStorage.Abstractions.Models;

/// <summary>完整扫描后当前目录的直接子项；目录日期取所有后代文件的最近修改日期，不持久化。</summary>
public sealed record ObjectStorageDirectoryScanResult(IReadOnlyList<ObjectStorageEntry> Entries, long FilesScanned);
