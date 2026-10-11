using Asgard.Hermod.Infrastructure.Storage;
using Asgard.Hermod.Services;

namespace Asgard.Hermod.UI.Tests;

/// <summary>资源服务测试使用真实组合路由和可控远程替身。</summary>
public static class ObjectStorageTestServices
{
    /// <summary>构造虚构第三方配置，UI 调用只需传入实例标识。</summary>
    public static (LocalObjectStorageService Service, FakeObjectStorageProvider Provider,
        ObjectStorageTestConfigurationService Configuration) Create(FakeObjectStorageClient client)
    {
        var configuration = new ObjectStorageTestConfigurationService(new InstanceConnectionOptions
        {
            TypeId = "storage",
            ProviderId = "test-provider",
            Parameters = new Dictionary<string, string> { ["apiToken"] = "private-resource-token" },
        });
        var provider = new FakeObjectStorageProvider("storage", "test-provider", client);
        return (new(configuration, new ObjectStorageClientFactory([provider])), provider, configuration);
    }
}
