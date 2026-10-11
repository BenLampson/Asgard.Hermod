namespace Asgard.Hermod.Infrastructure.Storage;

/// <summary>根据已解密的类型与服务商组合选择资源连接器，应用层不依赖 SDK。</summary>
public interface IObjectStorageClientFactory
{
    /// <summary>资源连接器缺失时明确失败，不把凭据转交其他类型或服务商。</summary>
    IObjectStorageClient CreateClient(InstanceConnectionOptions options, string? region = null);
}
