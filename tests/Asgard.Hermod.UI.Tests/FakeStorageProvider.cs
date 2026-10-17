namespace Asgard.Hermod.UI.Tests;

/// <summary>验证第三方连接器注册边界的简单替身，不访问真实网络。</summary>
public sealed class FakeStorageProvider : IStorageProvider
{
    /// <summary>通过稳定标识模拟任意第三方服务商。</summary>
    public FakeStorageProvider(string id)
    {
        Descriptor = new(id, "Third-party storage", "Future storage", "database",
            new("storage", "存储", "连接对象存储服务", "box"), []);
    }

    /// <inheritdoc />
    public InstanceProviderDescriptor Descriptor { get; }
    /// <summary>记录请求供隔离测试断言，诊断输出仍通过 options 脱敏实现。</summary>
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
