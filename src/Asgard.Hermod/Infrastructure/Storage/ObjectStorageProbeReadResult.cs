namespace Asgard.Hermod.Infrastructure.Storage;

/// <summary>探测回读的实际版本与有限正文，调用方负责释放 Content。</summary>
public sealed record ObjectStorageProbeReadResult(ObjectStorageReadResult Content, string? VersionId);
