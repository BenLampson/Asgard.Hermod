namespace Asgard.Hermod.Sync.Tests;

/// <summary>替代网络目录边界，验证不可确认权限时 API 必须拒绝。</summary>
internal sealed class TestUserAccessVerifier : IUserAccessVerifier
{
    /// <summary>控制最终身份状态。</summary>
    public bool Allowed { get; set; } = true;
    /// <summary>控制目录故障。</summary>
    public bool Unavailable { get; set; }
    /// <inheritdoc />
    public Task VerifyAsync(string subjectId, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new Asgard.Heimdall.Client.HeimdallClientException("resource_unavailable");
        if (!Allowed) throw new HermodApiException(403, "SyncForbidden");
        return Task.CompletedTask;
    }
}
