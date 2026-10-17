
namespace Asgard.Hermod.UI.Tests;

/// <summary>第三方资源连接器替身，记录类型、区域和解密参数的路由。</summary>
public sealed class FakeObjectStorageProvider(string typeId, string providerId, IObjectStorageClient client) : IObjectStorageProvider
{
    /// <inheritdoc />
    public string TypeId => typeId;
    /// <inheritdoc />
    public string ProviderId => providerId;
    /// <summary>最后一次用于创建客户端的参数，只作为测试断言数据。</summary>
    public InstanceConnectionOptions? ReceivedOptions { get; private set; }
    /// <summary>容器区域由应用服务显式传入。</summary>
    public string? ReceivedRegion { get; private set; }
    /// <inheritdoc />
    public IObjectStorageClient CreateClient(InstanceConnectionOptions options, string? region)
    {
        ReceivedOptions = options;
        ReceivedRegion = region;
        return client;
    }
}
