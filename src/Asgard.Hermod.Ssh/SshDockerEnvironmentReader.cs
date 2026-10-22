namespace Asgard.Hermod.Ssh;

/// <summary>同一 SSH 传输共享环境探测结果；重连必须重新探测，不跨主机复用版本。</summary>
public sealed class SshDockerEnvironmentReader
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ISshWorkspaceConnection? _owner;
    private SshDockerEnvironment? _environment;

    /// <summary>显式重新检测可刷新缓存；同一时刻只执行一个探测。</summary>
    public async Task<SshDockerEnvironment> ReadAsync(ISshWorkspaceConnection connection, bool force, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!force && ReferenceEquals(connection, _owner) && _environment is { } cached) return cached;
            var result = await connection.ExecuteCommandAsync(SshDockerEnvironment.ProbeCommand, token).WaitAsync(token).ConfigureAwait(false);
            var environment = SshDockerEnvironment.Parse(result);
            token.ThrowIfCancellationRequested(); _owner = connection; _environment = environment;
            return environment;
        }
        finally { _gate.Release(); }
    }
}
