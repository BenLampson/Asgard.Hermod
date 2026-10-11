namespace Asgard.Hermod.UI.Tests;

/// <summary>外壳验证使用安全的实例摘要，构造过程没有访问密钥或远程请求。</summary>
internal sealed class ObjectStorageWorkspaceDataSource : IWorkspaceDataSource
{
    /// <summary>已保存的对象存储实例。</summary>
    internal InstanceSummary Instance { get; set; } = new("oss-instance", "开发 OSS", "开发 OSS", "cloud", ["Development"],
        Endpoint: "阿里云 OSS", TypeId: "storage", ProviderId: "aliyun-oss");

    /// <inheritdoc />
    public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
    /// <inheritdoc />
    public ServiceSnapshot? GetServiceSnapshot(string instanceId) => null;
}
