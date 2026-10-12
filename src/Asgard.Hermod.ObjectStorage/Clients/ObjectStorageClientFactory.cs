namespace Asgard.Hermod.ObjectStorage.Clients;

/// <summary>通过 DI 索引对象存储扩展，保持资源操作与通用实例身份一致。</summary>
public sealed class ObjectStorageClientFactory : IObjectStorageClientFactory
{
    /// <summary>类型和服务商的组合索引，不以单独服务商名称推断类型。</summary>
    private readonly IReadOnlyDictionary<(string TypeId, string ProviderId), IObjectStorageProvider> _providers;

    /// <summary>重复组合在启动阶段失败，未来第三方只需注册资源连接器。</summary>
    public ObjectStorageClientFactory(IEnumerable<IObjectStorageProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var entries = new Dictionary<(string, string), IObjectStorageProvider>();
        foreach (IObjectStorageProvider provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (string.IsNullOrWhiteSpace(provider.TypeId) || string.IsNullOrWhiteSpace(provider.ProviderId) ||
                !entries.TryAdd((provider.TypeId, provider.ProviderId), provider))
                throw Text.Exception("InvalidOrDuplicateObjectStorageConnectorIdentifiersCheckRegistration");
        }
        _providers = entries;
    }

    /// <inheritdoc />
    public IObjectStorageClient CreateClient(InstanceConnectionOptions options, string? region = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!_providers.TryGetValue((options.TypeId, options.ProviderId), out IObjectStorageProvider? provider))
            throw ObjectStorageException.FromResource("TheObjectStorageResourceConnectorForThisInstanceIsNotInstalled", "UnsupportedProvider");
        return provider.CreateClient(options, region);
    }
}
