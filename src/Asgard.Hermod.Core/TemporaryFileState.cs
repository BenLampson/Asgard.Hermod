namespace Asgard.Hermod.Core;

/// <summary>清理前会重新验证的临时文件扫描结果。</summary>
internal sealed record TemporaryFileState(string Path, long Length, TemporaryFileCategory Category);
