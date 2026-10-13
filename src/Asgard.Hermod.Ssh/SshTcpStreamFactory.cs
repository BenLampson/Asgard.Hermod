using Tmds.Ssh;

namespace Asgard.Hermod.Ssh;

/// <summary>TCP 转发遵循终端连接的凭据和设备信任策略；不会自动接受未知主机。</summary>
public sealed class SshTcpStreamFactory(IHostKeyTrustStore trust, ISshHostKeyPrompt prompt) : ISshTcpStreamFactory
{
    /// <inheritdoc />
    public async Task<Stream> OpenAsync(InstanceConnectionOptions options, string host, int port, CancellationToken cancellationToken = default)
    {
        var profile = SshConnectionOptions.Parse(options);
        if (profile.Route != SshIds.Direct) throw Text.Exception("SshConfigurationInvalid");
        for (int attempt = 0; attempt < 4; attempt++)
        {
            SshHostKeyChallenge? challenge = null;
            var target = profile.Target;
            Credential credential = target.Authentication switch
            {
                SshIds.PrivateKey => new PrivateKeyCredential(target.KeyPath, target.Passphrase),
                SshIds.Agent => new SshAgentCredentials(),
                _ => new PasswordCredential(target.Password),
            };
            var client = new SshClient(new SshClientSettings
            {
                HostName = target.Host,
                Port = target.Port,
                UserName = target.User,
                Credentials = [credential],
                UserKnownHostsFilePaths = [],
                GlobalKnownHostsFilePaths = [],
                UpdateKnownHostsFileAfterAuthentication = false,
                AutoReconnect = false,
                AutoConnect = false,
                ConnectTimeout = TimeSpan.FromSeconds(profile.TimeoutSeconds),
                HostAuthentication = async (context, token) =>
                {
                    var key = context.ConnectionInfo.ServerKey.Key;
                    var presented = new TrustedHostKey(target.Host, target.Port, key.Type, "SHA256:" + key.SHA256FingerPrint);
                    var previous = await trust.LoadAsync(target.Host, target.Port, token).ConfigureAwait(false);
                    if (previous?.Algorithm == presented.Algorithm && previous.Fingerprint == presented.Fingerprint) return true;
                    challenge = new(presented, previous, false); return false;
                },
            });
            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                var stream = await client.OpenTcpConnectionAsync(host, port, cancellationToken).ConfigureAwait(false);
                return new SshOwnedStream(stream, client);
            }
            catch (Exception) when (challenge is not null && !cancellationToken.IsCancellationRequested)
            {
                client.Dispose();
                if (!await prompt.ConfirmAsync(challenge, cancellationToken).ConfigureAwait(false))
                    throw new SshException("SshHostNotTrusted", "SSH_HOST_NOT_TRUSTED");
                if (!await trust.TrustAsync(challenge.Presented, challenge.Previous?.Fingerprint, cancellationToken).ConfigureAwait(false))
                    throw new SshException("SshTrustChanged", "SSH_TRUST_CHANGED");
            }
            catch (Exception error) { client.Dispose(); cancellationToken.ThrowIfCancellationRequested(); throw SshConnectionFactory.SafeError(error); }
        }
        throw new SshException("SshTrustChanged", "SSH_TRUST_CHANGED");
    }
}
