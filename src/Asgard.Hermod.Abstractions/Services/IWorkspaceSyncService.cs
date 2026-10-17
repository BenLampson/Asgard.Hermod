namespace Asgard.Hermod.Abstractions.Services;

/// <summary>用户主动同步，不因登录自动迁移或覆盖本地工作区。</summary>
public interface IWorkspaceSyncService
{
    /// <summary>使用同步密码加密后上传，版本冲突时拒绝覆盖。</summary>
    Task UploadAsync(string password, CancellationToken cancellationToken = default);
    /// <summary>下载、校验与解密成功后才替换本地工作区。</summary>
    Task DownloadAsync(string password, CancellationToken cancellationToken = default);
}
