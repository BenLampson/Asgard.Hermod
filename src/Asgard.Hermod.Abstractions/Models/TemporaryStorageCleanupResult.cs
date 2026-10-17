namespace Asgard.Hermod.Abstractions.Models;

/// <summary>只统计确实删除的文件与字节，占用或无法访问的项目保留。</summary>
public sealed record TemporaryStorageCleanupResult(long FreedBytes, int DeletedFileCount, int SkippedFileCount);
