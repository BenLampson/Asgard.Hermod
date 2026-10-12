namespace Asgard.Hermod.Abstractions.Models;

/// <summary>描述实例类型下的具体服务及表单，界面不依赖具体 SDK。</summary>
public sealed record InstanceProviderDescriptor(string Id, string Name, string ProductName, string Icon,
    InstanceTypeDescriptor Type, IReadOnlyList<InstanceConfigurationField> Fields)
{
    /// <summary>由服务商说明实际验证操作，通用向导不假设验证只会读取远程资源。</summary>
    public string ConnectionTestDescription { get; init; } = Text.Get("CheckTheConnectionInformationAndEnsureYourIdentityHasTheRequiredAccessPermissions");
}
