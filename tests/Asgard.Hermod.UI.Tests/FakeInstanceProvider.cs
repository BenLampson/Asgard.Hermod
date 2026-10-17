namespace Asgard.Hermod.UI.Tests;

/// <summary>独立模拟非存储实例类型，不实现存储接口或复用 OSS 凭据字段。</summary>
public sealed class FakeInstanceProvider : IInstanceProvider
{
    /// <summary>测试可以提供类型描述和字段声明，验证注册、校验和持久化边界。</summary>
    public FakeInstanceProvider(InstanceProviderDescriptor descriptor) => Descriptor = descriptor;

    /// <inheritdoc />
    public InstanceProviderDescriptor Descriptor { get; }
    /// <summary>只记录类型和服务商都匹配的连接配置。</summary>
    public InstanceConnectionOptions? ReceivedOptions { get; private set; }

    /// <inheritdoc />
    public Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.TypeId != Descriptor.Type.Id || options.ProviderId != Descriptor.Id)
            return Task.FromResult(new InstanceConnectionTestResult(false, "fake mismatch", "instance_provider_mismatch"));
        ReceivedOptions = options;
        return Task.FromResult(new InstanceConnectionTestResult(true, "fake"));
    }
}
