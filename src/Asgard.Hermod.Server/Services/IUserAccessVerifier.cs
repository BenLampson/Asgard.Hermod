namespace Asgard.Hermod.Server;

/// <summary>只读在线权限门禁，不使用离线 JWT 猜测最终用户状态。</summary>
public interface IUserAccessVerifier
{
    /// <summary>只允许当前有效且具有 Hermod 同步权限的租户用户。</summary>
    Task VerifyAsync(string subjectId, CancellationToken cancellationToken);
}
