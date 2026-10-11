namespace Asgard.Hermod.UI.Services;

/// <summary>先发现顶层实例类型，再发现该类型下的具体服务，防止跨类型使用凭据。</summary>
public interface IInstanceProviderRegistry
{
    /// <summary>有已注册连接器的顶层实例类型。</summary>
    IReadOnlyList<InstanceTypeDescriptor> Types { get; }
    /// <summary>只返回所选实例类型下的具体服务。</summary>
    IReadOnlyList<InstanceProviderDescriptor> GetProviders(string typeId);
    /// <summary>通过类型与服务的组合定位连接器，未知或跨类型标识应报错。</summary>
    IInstanceProvider GetProvider(string typeId, string providerId);
}
