namespace Asgard.Hermod.Abstractions.Models;

/// <summary>选择范围时使用的 Bucket 名称与服务商返回的区域。</summary>
public sealed record McpStorageBucket(string Name, string Region);
