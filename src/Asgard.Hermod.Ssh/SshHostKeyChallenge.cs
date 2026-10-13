namespace Asgard.Hermod.Ssh;

/// <summary>可信渠道核验请求，只含公开主机身份。</summary>
public sealed record SshHostKeyChallenge(TrustedHostKey Presented, TrustedHostKey? Previous, bool IsJumpHost)
{
    /// <summary>已有身份变化时必须明确确认更新，不能当成首次连接。</summary>
    public bool HasChanged => Previous is not null;
}
