using System.Net.Sockets;
using Tmds.Ssh;

namespace Asgard.Hermod.Ssh;

/// <summary>连接时只使用用户选择的凭据，主机身份在每次认证前独立核验。</summary>
public sealed class SshConnectionFactory(IHostKeyTrustStore trust, ISshHostKeyPrompt prompt) : ISshConnectionFactory
{
    /// <inheritdoc />
    public Task<ISshConnection> OpenAsync(InstanceConnectionOptions options, int columns, int rows, CancellationToken cancellationToken = default) =>
        ConnectAsync(options, (client, bridge, profile, keys) => OpenShellAsync(client, bridge, profile, keys, columns, rows, cancellationToken),
            promptForTrust: true, cancellationToken);

    /// <inheritdoc />
    public Task<SshCommandResult> ExecuteDiagnosticAsync(InstanceConnectionOptions options, string command, CancellationToken cancellationToken = default) =>
        ConnectAsync(options, async (client, bridge, profile, keys) =>
        {
            // 交互式堡垒机必须完成已保存的目标导航。其他连接不启动登录 Shell，只打开独立 exec 通道。
            if (profile.Route == SshIds.Bastion)
            {
                await using var connection = await OpenShellAsync(client, bridge, profile, keys, 80, 24, cancellationToken).ConfigureAwait(false);
                return await ((ISshWorkspaceConnection)connection).ExecuteCommandAsync(command, cancellationToken).ConfigureAwait(false);
            }
            return await SshConnection.ExecuteBoundedAsync(client, command, cancellationToken).ConfigureAwait(false);
        }, promptForTrust: false, cancellationToken);

    private async Task<TResult> ConnectAsync<TResult>(InstanceConnectionOptions options,
        Func<SshClient, SshProxyBridge?, SshConnectionOptions, TrustedHostKey[], Task<TResult>> connect, bool promptForTrust,
        CancellationToken cancellationToken)
    {
        var profile = SshConnectionOptions.Parse(options);
        // 第一次跳板与目标分别核验；拒绝无限重试或自动覆盖变化的指纹。
        for (int attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SshHostKeyChallenge? challenge = null;
            var verified = new List<TrustedHostKey>();
            var settings = Settings(profile.Target, false);
            if (profile.JumpHost is { } jump) settings.Proxy = new SshProxy(Settings(jump, true));
            SshProxyBridge? bridge = null;
            SshClient? client = null;
            try
            {
                if (profile.Route == SshIds.Proxy)
                {
                    bridge = new SshProxyBridge(profile, cancellationToken);
                    settings.HostName = "127.0.0.1";
                    settings.Port = bridge.Port;
                }
                client = new SshClient(settings);
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                var result = await connect(client, bridge, profile, verified.ToArray()).ConfigureAwait(false);
                if (!promptForTrust && profile.Route != SshIds.Bastion)
                {
                    client.Dispose();
                    if (bridge is not null) await bridge.DisposeAsync().ConfigureAwait(false);
                }
                return result;
            }
            catch (Exception) when (challenge is not null && !cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                if (bridge is not null) await bridge.DisposeAsync().ConfigureAwait(false);
                // SDK 连接已终止，用户核验不受 SDK 的网络超时计时影响。
                bool accepted = promptForTrust && await prompt.ConfirmAsync(challenge, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!accepted) throw new SshException("SshHostNotTrusted", "SSH_HOST_NOT_TRUSTED");
                if (!await trust.TrustAsync(challenge.Presented, challenge.Previous?.Fingerprint, cancellationToken).ConfigureAwait(false))
                    throw new SshException("SshTrustChanged", "SSH_TRUST_CHANGED");
            }
            catch (Exception error)
            {
                client?.Dispose();
                Exception? proxyError = bridge?.Failure;
                if (bridge is not null) await bridge.DisposeAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw SafeError(proxyError ?? error);
            }

            SshClientSettings Settings(SshHostOptions host, bool isJump)
            {
                Credential credential = host.Authentication switch
                {
                    SshIds.PrivateKey => SshPrivateKey.Credential(host),
                    SshIds.Agent => new SshAgentCredentials(),
                    _ => new PasswordCredential(host.Password),
                };
                return new SshClientSettings
                {
                    HostName = host.Host,
                    Port = host.Port,
                    UserName = host.User,
                    Credentials = [credential],
                    UserKnownHostsFilePaths = [],
                    GlobalKnownHostsFilePaths = [],
                    UpdateKnownHostsFileAfterAuthentication = false,
                    AutoReconnect = false,
                    AutoConnect = false,
                    ConnectTimeout = TimeSpan.FromSeconds(profile.TimeoutSeconds),
                    KeepAliveInterval = TimeSpan.FromSeconds(30),
                    HostAuthentication = async (context, token) =>
                    {
                        var info = context.ConnectionInfo;
                        var presented = new TrustedHostKey(host.Host, host.Port, info.ServerKey.Key.Type,
                            "SHA256:" + info.ServerKey.Key.SHA256FingerPrint);
                        var previous = await trust.LoadAsync(host.Host, host.Port, token).ConfigureAwait(false);
                        if (previous?.Fingerprint == presented.Fingerprint && previous.Algorithm == presented.Algorithm)
                        { verified.Add(presented); return true; }
                        challenge = new(presented, previous, isJump);
                        return false;
                    },
                };
            }
        }
        throw new SshException("SshTrustChanged", "SSH_TRUST_CHANGED");
    }

    private static async Task<ISshConnection> OpenShellAsync(SshClient client, SshProxyBridge? bridge, SshConnectionOptions profile,
        TrustedHostKey[] keys, int columns, int rows, CancellationToken cancellationToken)
    {
        var shell = await client.ExecuteShellAsync(new ExecuteOptions
        {
            AllocateTerminal = true,
            TerminalType = "xterm-256color",
            TerminalSize = new(columns, rows),
        }, cancellationToken).ConfigureAwait(false);
        var connection = new SshConnection(client, shell, bridge, keys);
        return profile.Route == SshIds.Bastion
            ? await SshBastionConnection.CreateAsync(connection, profile, cancellationToken).ConfigureAwait(false)
            : connection;
    }

    /// <summary>只按异常类型归类，不解析或展示 SDK 的响应和消息。</summary>
    public static SshException SafeError(Exception error)
    {
        if (error is SshException safe) return safe;
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException or OperationCanceledException) return new("SshConnectionTimeout", "SSH_TIMEOUT");
            if (current is SocketException) return new("SshNetworkFailed", "SSH_NETWORK_FAILED");
            if (current is FileNotFoundException or UnauthorizedAccessException) return new("SshKeyUnavailable", "SSH_KEY_UNAVAILABLE");
        }
        return new("SshConnectionFailed", "SSH_CONNECTION_FAILED");
    }
}
