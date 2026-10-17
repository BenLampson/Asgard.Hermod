namespace Asgard.Hermod.Abstractions.Models;

/// <summary>云端配置集合；旧快照仅用于客户端解密迁移，密码校验包绑定当前账户。</summary>
public sealed record CloudConfiguration(string Revision, EncryptedWorkspace? Protection,
    IReadOnlyList<CloudInstanceConfiguration> Instances, IReadOnlyList<CloudTagConfiguration> Tags,
    DateTime? UpdatedAt, EncryptedWorkspace? LegacyWorkspace = null)
{
    /// <summary>独立账号密文；上传 null 表示旧客户端未提供，清除必须上传加密的空集合。</summary>
    public EncryptedWorkspace? AgentModels { get; init; }
}
