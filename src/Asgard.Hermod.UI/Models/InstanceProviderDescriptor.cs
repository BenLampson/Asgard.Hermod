namespace Asgard.Hermod.UI.Models;

/// <summary>描述实例类型下的具体服务及表单，界面不依赖具体 SDK。</summary>
public sealed record InstanceProviderDescriptor(string Id, string Name, string ProductName, string Icon,
    InstanceTypeDescriptor Type, IReadOnlyList<InstanceConfigurationField> Fields);
