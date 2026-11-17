namespace Asgard.Hermod.Abstractions.Models;

/// <summary>在同一个本地快照中读取归属和连接版本。</summary>
public sealed record McpWorkspace(string Owner, IReadOnlyList<McpTarget> Targets);
