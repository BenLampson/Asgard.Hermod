namespace Asgard.Hermod.Ssh;

/// <summary>连接测试与工作页共用同一协议工厂，不以模拟结果解锁保存。</summary>
public sealed class SshInstanceProvider(string route, ISshConnectionFactory connections) : IInstanceProvider
{
    /// <inheritdoc />
    public InstanceProviderDescriptor Descriptor { get; } = SshProfile.Descriptor(route);
    /// <inheritdoc />
    public async Task<InstanceConnectionOptions> PrepareConnectionAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    {
        _ = SshConnectionOptions.Parse(options);
        var values = new Dictionary<string, string>(options.Parameters, StringComparer.Ordinal);
        foreach (string prefix in route == SshIds.Jump ? new[] { "", "jump." } : [""])
            if (values.GetValueOrDefault(prefix + "auth") == SshIds.PrivateKey)
                values[prefix + "key"] = await SshPrivateKey.ImportAsync(values[prefix + "key"], cancellationToken).ConfigureAwait(false);
        return new() { TypeId = options.TypeId, ProviderId = options.ProviderId, Parameters = values };
    }
    /// <inheritdoc />
    public async Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    {
        if (options.ProviderId != route) throw Text.Exception("SshConfigurationInvalid");
        try
        {
            await using var connection = await connections.OpenAsync(options, 80, 24, cancellationToken).ConfigureAwait(false);
            var checks = connection.HostKeys.Select((_, index) => new InstanceConnectionCheck(
                Text.Get(route == SshIds.Jump && index == 0 ? "SshJumpVerified" : "SshTargetVerified"), true)).ToList();
            checks.Add(new(Text.Get("SshAuthenticationVerified"), true));
            checks.Add(new(Text.Get("SshTerminalVerified"), true));
            return new(true, Text.Get("SshTestPassed"), Checks: checks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var safe = SshConnectionFactory.SafeError(error);
            return new(false, Text.Error(safe, "SshConnectionFailed"), safe.Code);
        }
    }
}
