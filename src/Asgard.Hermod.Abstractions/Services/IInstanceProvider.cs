namespace Asgard.Hermod.Abstractions.Services;

/// <summary>顶层实例连接器合同，存储、Docker 等类型统一通过此入口配置和验证。</summary>
public interface IInstanceProvider
{
    /// <summary>实例类型、具体服务和其专属表单字段。</summary>
    InstanceProviderDescriptor Descriptor { get; }
    /// <summary>以只读操作验证当前类型及连接参数，不修改远程资源。</summary>
    Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options,
        CancellationToken cancellationToken = default);
}
