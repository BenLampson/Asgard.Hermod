namespace Asgard.Hermod.Abstractions.Models;

/// <summary>云端配置集合；旧快照仅用于客户端解密迁移，密码校验包绑定当前账户。</summary>
public sealed record CloudConfiguration(string Revision, EncryptedWorkspace? Protection,
    IReadOnlyList<CloudInstanceConfiguration> Instances, IReadOnlyList<CloudTagConfiguration> Tags,
    DateTime? UpdatedAt, EncryptedWorkspace? LegacyWorkspace = null);
