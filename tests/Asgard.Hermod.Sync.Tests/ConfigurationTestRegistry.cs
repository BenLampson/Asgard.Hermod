namespace Asgard.Hermod.Sync.Tests;

/// <summary>声明可读字段、敏感字段与未知字段的真实加密边界。</summary>
internal sealed class ConfigurationTestRegistry : IInstanceProviderRegistry
{
    private static readonly InstanceTypeDescriptor Type = new("storage", "Storage", "Storage", "database");
    public IReadOnlyList<InstanceTypeDescriptor> Types => [Type];
    public IReadOnlyList<InstanceProviderDescriptor> GetProviders(string typeId) => typeId != Type.Id ? [] :
        [new("provider", "Provider", "Provider", "database", Type,
            [new("endpoint", "Endpoint"), new("accessKeyId", "Key ID", IsSensitive: true), new("secret", "Secret", InstanceFieldKind.Secret)])];
    public IInstanceProvider GetProvider(string typeId, string providerId) => throw new NotSupportedException();
}
