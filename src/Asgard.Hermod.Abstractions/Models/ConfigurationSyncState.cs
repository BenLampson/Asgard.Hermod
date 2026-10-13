namespace Asgard.Hermod.Abstractions.Models;

/// <summary>按账户保护的本机密码、同步基准和离线副本，不发送给服务器。</summary>
public sealed record ConfigurationSyncState(string? Password = null, WorkspaceSnapshot? Baseline = null,
    WorkspaceSnapshot? WorkingCopy = null, string Revision = "0", DateTime? LastSyncedAt = null)
{
    /// <summary>禁止调试输出密码或工作区内容。</summary>
    public override string ToString() => nameof(ConfigurationSyncState);
}
