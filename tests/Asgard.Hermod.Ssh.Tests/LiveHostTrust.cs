using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.Abstractions.Services;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>真实 SSH 测试的内存设备信任边界，不使用或改写用户 known_hosts。</summary>
internal sealed class LiveHostTrust : IHostKeyTrustStore, ISshHostKeyPrompt
{
    private readonly Dictionary<(string, int), TrustedHostKey> _keys = [];
    internal List<SshHostKeyChallenge> Challenges { get; } = [];
    internal bool Accept { get; set; } = true;
    internal Func<CancellationToken, Task<bool>>? Prompt { get; set; }
    public Task<TrustedHostKey?> LoadAsync(string host, int port, CancellationToken cancellationToken = default) => Task.FromResult(_keys.GetValueOrDefault((host, port)));
    public Task<bool> TrustAsync(TrustedHostKey key, string? expectedFingerprint, CancellationToken cancellationToken = default)
    {
        if (_keys.GetValueOrDefault((key.Host, key.Port))?.Fingerprint != expectedFingerprint) return Task.FromResult(false);
        _keys[(key.Host, key.Port)] = key; return Task.FromResult(true);
    }
    public Task<bool> ConfirmAsync(SshHostKeyChallenge challenge, CancellationToken cancellationToken = default)
    { Challenges.Add(challenge); return Prompt?.Invoke(cancellationToken) ?? Task.FromResult(Accept); }
}
