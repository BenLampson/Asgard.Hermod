namespace Asgard.Hermod.UI.Models;

/// <summary>顶层实例类型，存储、Docker 等各自拥有自己的连接器与配置字段。</summary>
public sealed record InstanceTypeDescriptor(string Id, string Name, string Description, string Icon);
