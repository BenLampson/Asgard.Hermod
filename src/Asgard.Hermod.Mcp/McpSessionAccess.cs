namespace Asgard.Hermod.Mcp;

/// <summary>连接接受时冻结策略，改动后旧请求不能使用新授权。</summary>
internal sealed record McpSessionAccess(McpLocalPolicy Policy, CancellationToken Revoked);
