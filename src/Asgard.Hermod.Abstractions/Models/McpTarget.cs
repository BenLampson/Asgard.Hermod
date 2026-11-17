namespace Asgard.Hermod.Abstractions.Models;

/// <summary>仅供授权界面识别连接，不包含密文、凭据或连接地址。</summary>
public sealed record McpTarget(string Id, string Name, string TypeId, string Revision);
