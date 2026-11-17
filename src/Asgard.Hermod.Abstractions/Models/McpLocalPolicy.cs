namespace Asgard.Hermod.Abstractions.Models;

/// <summary>本机策略及配对凭证，只允许授权服务及受保护的持久化层读取。</summary>
public sealed record McpLocalPolicy(string Owner, string Generation, bool Enabled, string Secret, IReadOnlyList<McpGrant> Grants)
{
    /// <inheritdoc />
    public override string ToString() => nameof(McpLocalPolicy);
}
