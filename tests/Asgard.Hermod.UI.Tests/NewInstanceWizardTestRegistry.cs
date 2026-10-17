namespace Asgard.Hermod.UI.Tests;

/// <summary>按稳定标识发现测试连接器，验证向导不硬编码具体 SDK 或服务商实例。</summary>
internal sealed class NewInstanceWizardTestRegistry : IInstanceProviderRegistry
{
    private readonly IReadOnlyDictionary<(string TypeId, string ProviderId), IInstanceProvider> _providers;

    /// <summary>每个用例独立构造自己的服务商集合。</summary>
    internal NewInstanceWizardTestRegistry(params IInstanceProvider[] providers)
    {
        _providers = providers.ToDictionary(provider => (provider.Descriptor.Type.Id, provider.Descriptor.Id));
        Types = providers.Select(provider => provider.Descriptor.Type)
            .DistinctBy(type => type.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>向导能够列出全部注册连接器。</summary>
    public IReadOnlyList<InstanceTypeDescriptor> Types { get; }
    /// <summary>服务商列表严格限制在所选顶层类型。</summary>
    public IReadOnlyList<InstanceProviderDescriptor> GetProviders(string typeId) => _providers.Values
        .Where(provider => provider.Descriptor.Type.Id == typeId).Select(provider => provider.Descriptor).ToArray();
    /// <summary>测试按类型和服务商组合定位连接器，不能跨类型调用。</summary>
    public IInstanceProvider GetProvider(string typeId, string providerId) => _providers[(typeId, providerId)];
}
