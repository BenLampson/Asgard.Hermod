namespace Asgard.Hermod.Core;

/// <summary>一个隔离目录作为清理单元，其中任何文件占用都会保护整个请求。</summary>
internal sealed record TemporaryRequestState(string Directory, IReadOnlyList<TemporaryFileState> Files, bool InUse);
