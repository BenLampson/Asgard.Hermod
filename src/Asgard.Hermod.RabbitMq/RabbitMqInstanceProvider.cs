using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.RabbitMq;

/// <summary>只检查认证、版本和可见虚拟主机，不声明、发布或消费资源。</summary>
public sealed class RabbitMqInstanceProvider(string route, IRabbitMqSessionFactory factory) : IInstanceProvider
{
    /// <inheritdoc />
    public InstanceProviderDescriptor Descriptor { get; } = RabbitMqProfile.Descriptor(route);
    /// <summary>在测试与保存之前导入隧道私钥，沿用 SSH 的文件限制与已有凭据加密同步。</summary>
    public async Task<InstanceConnectionOptions> PrepareConnectionAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.ProviderId != route) throw Text.Exception("RabbitMqConfigurationInvalid");
        var parsed = RabbitMqSessionOptions.Parse(options);
        if (!parsed.UsesSsh) return parsed.Connection;
        var ssh = await SshConnectionPreparation.PrepareAsync(parsed.SshConnection, cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<string, string>(parsed.Connection.Parameters, StringComparer.Ordinal);
        foreach (var pair in ssh.Parameters) values["ssh." + pair.Key] = pair.Value;
        return new() { TypeId = options.TypeId, ProviderId = options.ProviderId, Parameters = values };
    }
    /// <inheritdoc />
    public async Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    {
        if (options.ProviderId != route) throw Text.Exception("RabbitMqConfigurationInvalid");
        try
        {
            await using var session = await factory.OpenAsync(options, cancellationToken).ConfigureAwait(false);
            var hosts = await session.ReadPageAsync(RabbitMqResource.Vhosts, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new(true, Text.Get(hosts.Total == 0 ? "RabbitMqTestNoVhosts" : "RabbitMqTestPassed"), Checks:
                [new(Text.Get("RabbitMqApiVerified"), true), new(Text.Get("RabbitMqAuthenticationVerified"), true),
                 new(Text.Format("RabbitMqVisibleVhosts", hosts.Total), true)]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { return new(false, Text.Error(error, "RabbitMqConnectionFailed")); }
    }
}
