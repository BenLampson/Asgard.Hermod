using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.MySql;

/// <summary>无池化、无自动重试，避免会话事务混用和写入重放。</summary>
public sealed class MySqlSessionFactory(ISshTcpStreamFactory ssh) : IMySqlSessionFactory
{
    public async Task<IMySqlSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    {
        var profile = MySqlSessionOptions.Parse(options); var owned = new List<IDisposable>();
        MySqlSshBridge? bridge = null; MySqlConnection? connection = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(profile.ConnectTimeout));
        try
        {
            if (profile.UsesSsh)
            {
                var parameters = options.Parameters.Where(pair => pair.Key.StartsWith("ssh.", StringComparison.Ordinal)).ToDictionary(pair => pair.Key[4..], pair => pair.Value);
                parameters["timeout"] = profile.ConnectTimeout.ToString(CultureInfo.InvariantCulture);
                var sshOptions = new InstanceConnectionOptions { TypeId = SshIds.Type, ProviderId = SshIds.Direct, Parameters = parameters };
                Task<Stream> Open(CancellationToken token) => ssh.OpenAsync(sshOptions, profile.Host, profile.Port, token);
                var first = await Open(timeout.Token).ConfigureAwait(false); bridge = new(first, Open);
            }
            var builder = new MySqlConnectionStringBuilder
            {
                Server = bridge is null ? profile.Host : "127.0.0.1",
                Port = (uint)(bridge?.Port ?? profile.Port),
                UserID = profile.User,
                Password = profile.Value("password"),
                Database = profile.Database,
                ConnectionTimeout = (uint)profile.ConnectTimeout,
                DefaultCommandTimeout = (uint)profile.CommandTimeout,
                CancellationTimeout = 5,
                Pooling = false,
                AutoEnlist = false,
                AllowLoadLocalInfile = false,
                AllowPublicKeyRetrieval = false,
                PersistSecurityInfo = false,
                ApplicationName = "Hermod",
                GuidFormat = MySqlGuidFormat.None,
                // Required 防止明文降级；VerifyFull 使用上述逻辑主机和系统/自定义信任回调。
                SslMode = profile.TlsMode == "disabled" ? MySqlSslMode.Disabled : MySqlSslMode.Required,
            };
            connection = new(builder.ConnectionString); MySqlTls.Configure(connection, profile, owned);
            await connection.OpenAsync(timeout.Token).ConfigureAwait(false);
            return new MySqlSession(connection, profile, owned, bridge);
        }
        catch (Exception error)
        {
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            if (bridge is not null) await bridge.DisposeAsync().ConfigureAwait(false); owned.ForEach(item => item.Dispose());
            cancellationToken.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested) throw new MySqlFailure("MySqlConnectionTimeout", "MYSQL_TIMEOUT");
            throw MySqlFailure.Safe(error);
        }
    }
}
