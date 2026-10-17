namespace Asgard.Hermod.MySql;

/// <summary>连接测试只建立会话、选择可选数据库和读取常量，不修改业务数据。</summary>
public sealed class MySqlInstanceProvider(string route, IMySqlSessionFactory factory) : IInstanceProvider
{
    /// <summary>语言初始化后创建的公共连接器描述。</summary>
    public InstanceProviderDescriptor Descriptor { get; } = MySqlProfile.Descriptor(route);
    /// <summary>验证路由和数据库访问，不修改业务数据。</summary>
    public async Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    {
        if (options.ProviderId != route) throw Text.Exception("MySqlConfigurationInvalid");
        try
        {
            await using var session = await factory.OpenAsync(options, cancellationToken).ConfigureAwait(false);
            var probe = await session.ExecuteAsync("SELECT 1", session.Options.Database, cancellationToken).ConfigureAwait(false);
            if (probe.Failure is { } failure) throw failure;
            return new(true, Text.Get("MySqlTestPassed"), Checks: [new(Text.Get("MySqlNetworkVerified"), true),
                new(Text.Get(session.Options.TlsMode == "disabled" ? "MySqlPlaintextConnection" : "MySqlTlsVerified"), true),
                new(Text.Get("MySqlAuthenticationVerified"), true), new(Text.Get("MySqlDatabaseVerified"), true)]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { var safe = MySqlFailure.Safe(error); return new(false, Text.Error(safe, "MySqlConnectionFailed"), safe.Code); }
    }
}
