namespace Asgard.Hermod.UI.Tests;

/// <summary>仅用于验证通用向导的同级 Docker 类型，不注册生产服务或连接真实引擎。</summary>
internal sealed class NewInstanceWizardTestDockerProvider : IInstanceProvider
{
    /// <summary>Docker 与存储属于同一级实例类型。</summary>
    public static InstanceTypeDescriptor DockerType { get; } = new("docker", "Docker", "容器引擎", "container");
    /// <summary>不同类型使用不同参数，令牌为可选字段。</summary>
    public InstanceProviderDescriptor Descriptor { get; } = new("docker-engine", "Docker Engine", "Docker API", "container", DockerType,
        [new("endpoint", "Docker 地址", MaxLength: 512, Hint: "连接 Docker 引擎的地址。"),
         new("apiToken", "API Token", InstanceFieldKind.Secret, IsRequired: false, MaxLength: 256, IsSensitive: true)]);
    /// <summary>记录通用测试入口是否正确调用此类型的连接器。</summary>
    public int CallCount { get; private set; }
    /// <summary>记录收到的类型、服务和独立字段。</summary>
    public InstanceConnectionOptions? LastConnection { get; private set; }

    /// <summary>返回确定性的成功结果，不产生外部副作用。</summary>
    public Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        LastConnection = options;
        return Task.FromResult(new InstanceConnectionTestResult(true, "Docker 连接通过", Checks: [new("Docker API 访问", true)]));
    }
}
