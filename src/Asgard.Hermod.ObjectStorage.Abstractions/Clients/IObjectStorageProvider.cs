namespace Asgard.Hermod.ObjectStorage.Abstractions.Clients;

/// <summary>资源连接器扩展入口，连接凭据只在所属类型和服务商内使用。</summary>
public interface IObjectStorageProvider
{
    /// <summary>对应通用实例注册表中的类型标识。</summary>
    string TypeId { get; }
    /// <summary>对应通用实例注册表中的服务商标识。</summary>
    string ProviderId { get; }
    /// <summary>为一次业务操作创建隔离客户端；连接器负责验证容器区域及自建服务地址。</summary>
    IObjectStorageClient CreateClient(InstanceConnectionOptions options, string? region);
}
