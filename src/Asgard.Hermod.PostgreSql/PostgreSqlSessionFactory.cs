using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.PostgreSql;

/// <summary>每个查询页签持有独立物理连接，不池化、不自动重连或重放。</summary>
public sealed class PostgreSqlSessionFactory(ISshTcpStreamFactory ssh) : IPostgreSqlSessionFactory
{
    /// <summary>建立独立会话，TLS 始终验证逻辑数据库主机名。</summary>
    public async Task<IPostgreSqlSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    {
        var profile = PostgreSqlSessionOptions.Parse(options); var owned = new List<IDisposable>();
        PostgreSqlSshBridge? bridge = null; NpgsqlConnection? connection = null; NpgsqlDataSource? source = null;
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
            var settings = new NpgsqlConnectionStringBuilder
            {
                Host = bridge is null ? profile.Host : "127.0.0.1",
                Port = bridge?.Port ?? profile.Port,
                Username = profile.User,
                Password = profile.Value("password"),
                Database = profile.Database,
                Timeout = profile.ConnectTimeout,
                CommandTimeout = profile.CommandTimeout,
                CancellationTimeout = 5000,
                Pooling = false,
                Multiplexing = false,
                Enlist = false,
                PersistSecurityInfo = false,
                IncludeErrorDetail = false,
                LogParameters = false,
                ApplicationName = "Hermod",
                // 回调验证逻辑主机和证书链；Require 禁止明文降级。
                SslMode = profile.TlsMode == "disabled" ? SslMode.Disable : SslMode.Require,
                GssEncryptionMode = GssEncryptionMode.Disable
            };
            var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
            PostgreSqlTls.Configure(builder, profile, owned); source = builder.Build();
            connection = await source.OpenConnectionAsync(timeout.Token).ConfigureAwait(false);
            return new PostgreSqlSession(connection, source, profile, owned, bridge, this);
        }
        catch (Exception error)
        {
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            if (source is not null) await source.DisposeAsync().ConfigureAwait(false);
            if (bridge is not null) await bridge.DisposeAsync().ConfigureAwait(false);
            owned.ForEach(item => item.Dispose()); cancellationToken.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested) throw new PostgreSqlFailure("PostgreSqlConnectionTimeout", "POSTGRESQL_TIMEOUT");
            throw PostgreSqlFailure.Safe(error);
        }
    }
}
