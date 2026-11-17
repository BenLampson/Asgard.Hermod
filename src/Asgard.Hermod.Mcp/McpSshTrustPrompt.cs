using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.Mcp;

/// <summary>外部工具只能使用已经信任的 SSH 主机，不能弹出或批准新的指纹。</summary>
internal sealed class McpSshTrustPrompt : ISshHostKeyPrompt
{
    public Task<bool> ConfirmAsync(SshHostKeyChallenge challenge, CancellationToken cancellationToken) => Task.FromResult(false);
}
