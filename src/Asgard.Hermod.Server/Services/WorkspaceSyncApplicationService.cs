using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Asgard.Hermod.Server;

/// <summary>身份和在线目录门禁通过后才访问个人同步分区。</summary>
public sealed class WorkspaceSyncApplicationService(CurrentHermodUser current, IUserAccessVerifier verifier,
    ICloudWorkspaceRepository repository, IOptions<HermodServerOptions> options)
{
    /// <summary>当前用户只展示 JWT 用户名，不使用邮箱或主体标识作为名称。</summary>
    public async Task<HermodAccount> GetAccountAsync(CancellationToken cancellationToken)
    {
        var account = current.Get(); await verifier.VerifyAsync(account.SubjectId, cancellationToken); return account;
    }
    /// <summary>读取当前身份自己的密文；不存在时返回版本零。</summary>
    public async Task<CloudWorkspace> ReadAsync(CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(cancellationToken);
        var stored = await Task.Run(() => repository.Read(Owner(account), cancellationToken), cancellationToken);
        return stored is null ? new("0", null, null) : ToSnapshot(stored);
    }
    /// <summary>上传结构验证和版本冲突均使用稳定安全错误，密码与明文永不进入服务器。</summary>
    public async Task<CloudWorkspace> UploadAsync(WorkspaceUpload upload, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(cancellationToken);
        if (upload is null || string.IsNullOrWhiteSpace(upload.ExpectedRevision) || upload.ExpectedRevision.Length > 64)
            throw new HermodApiException(400, "SyncSnapshotInvalid");
        WorkspaceEncryption.ValidateEnvelope(upload.Workspace);
        var stored = await Task.Run(() => repository.Save(Owner(account), account, upload.ExpectedRevision,
            JsonSerializer.Serialize(upload.Workspace), cancellationToken), cancellationToken);
        return stored is null ? throw new HermodApiException(409, "SyncConflict") : ToSnapshot(stored);
    }
    /// <summary>明细接口保持同样的身份与在线目录门禁。</summary>
    public async Task<CloudConfiguration> ReadConfigurationAsync(CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(cancellationToken);
        return await Task.Run(() => repository.ReadConfiguration(Owner(account), cancellationToken), cancellationToken);
    }
    /// <summary>验证后原子提交明细；版本冲突不覆盖任何条目。</summary>
    public async Task<CloudConfiguration> UploadConfigurationAsync(ConfigurationUpload upload, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(cancellationToken);
        ConfigurationValidation.Validate(upload);
        return await Task.Run(() => repository.SaveConfiguration(Owner(account), account, upload, cancellationToken), cancellationToken)
            ?? throw new HermodApiException(409, "SyncConflict");
    }
    /// <summary>发行者、租户、应用和主体共同限定服务器归属，不依赖可变用户名。</summary>
    private string Owner(HermodAccount account) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new[] { options.Value.Authority, account.TenantId, account.ApplicationId, account.SubjectId }))));
    /// <summary>持久化记录转换为公共输出模型，不返回内部归属索引。</summary>
    private static CloudWorkspace ToSnapshot(StoredCloudWorkspace stored) => new(stored.Revision,
        JsonSerializer.Deserialize<EncryptedWorkspace>(stored.ProtectedJson), stored.UpdatedAt);
}
