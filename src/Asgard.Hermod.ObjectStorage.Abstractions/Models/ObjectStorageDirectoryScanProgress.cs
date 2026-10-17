namespace Asgard.Hermod.ObjectStorage.Abstractions.Models;

/// <summary>递归枚举的实际进度，不包含服务商未提供的总数或百分比。</summary>
public sealed record ObjectStorageDirectoryScanProgress(long FilesScanned, long PagesRead);
