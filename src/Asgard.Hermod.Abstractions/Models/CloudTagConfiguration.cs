namespace Asgard.Hermod.Abstractions.Models;

/// <summary>标签内容和服务器生成的独立版本。</summary>
public sealed record CloudTagConfiguration(SyncedTag Tag, string Version);
