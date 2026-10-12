namespace Asgard.Hermod.Identity;

/// <summary>受设备保护的会话绑定原发行者和客户端，不跨环境复用令牌。</summary>
internal sealed record SavedAccountSession(string Authority, string ClientId, string SubjectId, string? SessionId, HeimdallTokenSet Tokens, DateTimeOffset ExpiresAt)
{
    /// <summary>禁止通过默认记录格式输出令牌。</summary>
    public override string ToString() => nameof(SavedAccountSession);
}
