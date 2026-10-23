namespace Asgard.Hermod.Abstractions.Services;

/// <summary>官方公开稳定版本的说明；不包含可执行的远程链接或安装指令。</summary>
public sealed record PublishedApplicationRelease(Version Version, string Notes);
