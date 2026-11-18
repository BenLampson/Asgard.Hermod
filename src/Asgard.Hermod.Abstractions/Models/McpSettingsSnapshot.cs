namespace Asgard.Hermod.Abstractions.Models;

/// <summary>设置页可编辑的本机授权和可选实例，不包含配对凭证。</summary>
public sealed record McpSettingsSnapshot(McpWorkspace Workspace, bool Enabled, IReadOnlyList<McpGrant> Grants,
    IReadOnlyList<McpExclusion>? Exclusions = null, bool DefaultAllow = true, IReadOnlyList<McpInstanceApproval>? InstanceApprovals = null);
