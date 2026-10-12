namespace Asgard.Hermod.Infrastructure.Storage;

/// <summary>保留探测写入的服务端版本回执；暂停版本化容器的 null 版本保留为字面字符串。</summary>
public sealed record ObjectStorageProbeWriteResult(string? VersionId);
