namespace Asgard.Hermod.Infrastructure.Instances;

/// <summary>通过 DI 发现各类实例连接器，以类型和服务商的组合隔离实现。</summary>
public sealed class InstanceProviderRegistry : IInstanceProviderRegistry
{
    /// <summary>相同服务商标识可以出现在不同类型中，两个标识都采用精确比较。</summary>
    private readonly IReadOnlyDictionary<(string TypeId, string ProviderId), IInstanceProvider> _providers;
    /// <summary>每个类型的描述列表用于动态表单，调用者不能修改注册表。</summary>
    private readonly IReadOnlyDictionary<string, IReadOnlyList<InstanceProviderDescriptor>> _descriptors;

    /// <summary>新类型只需注册 IInstanceProvider；无效或冲突的元数据在启动阶段失败。</summary>
    public InstanceProviderRegistry(IEnumerable<IInstanceProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var entries = new Dictionary<(string, string), IInstanceProvider>();
        var types = new Dictionary<string, InstanceTypeDescriptor>(StringComparer.Ordinal);
        var descriptors = new Dictionary<string, List<InstanceProviderDescriptor>>(StringComparer.Ordinal);
        foreach (IInstanceProvider provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            InstanceProviderDescriptor descriptor = provider.Descriptor;
            if (descriptor is null || descriptor.Type is null || !IsValidKey(descriptor.Id, 80) ||
                !IsValidKey(descriptor.Type.Id, 80))
                throw new InvalidOperationException("实例类型与服务商必须提供有效的稳定标识。");
            if (types.TryGetValue(descriptor.Type.Id, out InstanceTypeDescriptor? knownType) && knownType != descriptor.Type)
                throw new InvalidOperationException("同一实例类型的描述必须一致，请检查连接器注册。");
            ValidateFields(descriptor.Fields);
            if (!entries.TryAdd((descriptor.Type.Id, descriptor.Id), provider))
                throw new InvalidOperationException("同一实例类型存在重复的服务商标识，请检查连接器注册。");
            types.TryAdd(descriptor.Type.Id, descriptor.Type);
            if (!descriptors.TryGetValue(descriptor.Type.Id, out List<InstanceProviderDescriptor>? typeProviders))
                descriptors.Add(descriptor.Type.Id, typeProviders = []);
            typeProviders.Add(descriptor with { Fields = Array.AsReadOnly(descriptor.Fields.ToArray()) });
        }
        _providers = entries;
        _descriptors = descriptors.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<InstanceProviderDescriptor>)Array.AsReadOnly(pair.Value.ToArray()), StringComparer.Ordinal);
        Types = Array.AsReadOnly(types.Values.ToArray());
    }

    /// <inheritdoc />
    public IReadOnlyList<InstanceTypeDescriptor> Types { get; }

    /// <inheritdoc />
    public IReadOnlyList<InstanceProviderDescriptor> GetProviders(string typeId)
    {
        if (typeId is null || !_descriptors.TryGetValue(typeId, out IReadOnlyList<InstanceProviderDescriptor>? descriptors))
            throw new InvalidOperationException("此实例类型未注册，请安装对应类型的连接器。");
        return descriptors;
    }

    /// <inheritdoc />
    public IInstanceProvider GetProvider(string typeId, string providerId)
    {
        if (typeId is null || providerId is null || !_providers.TryGetValue((typeId, providerId), out IInstanceProvider? provider))
            throw new InvalidOperationException("此实例类型的连接器未注册，请检查服务商配置或安装对应连接器。");
        return provider;
    }

    /// <summary>字段标识用于字典键和持久化，不允许空白、控制字符或重复标识。</summary>
    private static void ValidateFields(IReadOnlyList<InstanceConfigurationField> fields)
    {
        if (fields is null || fields.Count > 100)
            throw new InvalidOperationException("连接器必须提供有效的配置字段列表。");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (InstanceConfigurationField field in fields)
        {
            if (field is null || !IsValidKey(field.Key, 128) || !keys.Add(field.Key) ||
                string.IsNullOrWhiteSpace(field.Label) || field.Label.Any(char.IsControl) ||
                field.MaxLength is < 0 or > 65536 || !Enum.IsDefined(field.Kind) || !Enum.IsDefined(field.ValidationRule))
                throw new InvalidOperationException("连接器配置字段无效或重复，请检查字段声明。");
        }
    }

    /// <summary>稳定标识限定为通用 ASCII 字符，不依赖语言或文化区域。</summary>
    private static bool IsValidKey(string? value, int maxLength) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maxLength && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
