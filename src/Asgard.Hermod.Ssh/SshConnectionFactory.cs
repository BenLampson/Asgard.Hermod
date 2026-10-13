using System.Net.Sockets;
using Tmds.Ssh;

namespace Asgard.Hermod.Ssh;

/// <summary>连接时只使用用户选择的凭据，主机身份在每次认证前独立核验。</summary>
public sealed class SshConnectionFactory(IHostKeyTrustStore trust, ISshHostKeyPrompt prompt) : ISshConnectionFactory
{
    /// <inheritdoc />
    public async Task<ISshConnection> OpenAsync(InstanceConnectionOptions options, int columns, int rows, CancellationToken cancellationToken = default)
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
                var shell = await client.ExecuteShellAsync(new ExecuteOptions
                {
                    AllocateTerminal = true,
                    TerminalType = "xterm-256color",
                    TerminalWidth = columns,
                    TerminalHeight = rows,
                }, cancellationToken).ConfigureAwait(false);
                return new SshConnection(client, shell, bridge, verified.ToArray());
            }
            catch (Exception) when (challenge is not null && !cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                if (bridge is not null) await bridge.DisposeAsync().ConfigureAwait(false);
                // SDK 连接已终止，用户核验不受 SDK 的网络超时计时影响。
                bool accepted = await prompt.ConfirmAsync(challenge, cancellationToken).ConfigureAwait(false);
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
                if (host.Authentication == SshIds.PrivateKey && !File.Exists(host.KeyPath))
                    throw new SshException("SshKeyUnavailable", "SSH_KEY_UNAVAILABLE");
                Credential credential = host.Authentication switch
                {
                    SshIds.PrivateKey => new PrivateKeyCredential(host.KeyPath, host.Passphrase),
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
