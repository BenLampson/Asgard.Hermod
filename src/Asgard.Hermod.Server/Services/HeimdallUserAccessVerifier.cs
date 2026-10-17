namespace Asgard.Hermod.Server;

/// <summary>官方 SDK 使用服务器 Client 查询其绑定应用中的最终权限，每次请求都检查。</summary>
public sealed class HeimdallUserAccessVerifier(HeimdallClientFactory clients) : IUserAccessVerifier
{
    /// <inheritdoc />
    public async Task VerifyAsync(string subjectId, CancellationToken cancellationToken)
    {
        using var directory = clients.Create("hermod-directory");
        var permissions = await directory.GetUserPermissionsAsync(subjectId, cancellationToken);
        if (!permissions.HasAllPermissions(["hermod.sync"])) throw new HermodApiException(403, "SyncForbidden");
    }
}
