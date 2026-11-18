namespace Asgard.Hermod.Abstractions.Models;

/// <summary>用户明确禁止的实例或资源；空资源表示禁止整个实例，子范围使用协议原名。</summary>
public sealed record McpExclusion(string Id, string InstanceId, string TypeId, string Resource = "", string Qualifier = "", string Table = "");
