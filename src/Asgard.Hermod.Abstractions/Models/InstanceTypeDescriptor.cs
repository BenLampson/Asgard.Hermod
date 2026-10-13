namespace Asgard.Hermod.Abstractions.Models;

/// <summary>顶层实例类型，存储、Docker 等各自拥有自己的连接器与配置字段。</summary>
public sealed record InstanceTypeDescriptor(string Id, string Name, string Description, string Icon)
{
    /// <summary>类型自行定义首步语义，公共向导不理解协议。</summary>
    public string ProviderSelectionTitleResourceKey { get; init; } = "ChooseProvider";
    /// <summary>首步整句说明对应的共享资源键。</summary>
    public string ProviderSelectionDescriptionResourceKey { get; init; } = "ChooseProviderForInstanceType";
    /// <summary>首步列表标签对应的共享资源键。</summary>
    public string ProviderLabelResourceKey { get; init; } = "Provider";
    /// <summary>类型明确声明同一实例是否允许切换连接路径，默认仍锁定服务商。</summary>
    public bool AllowProviderChangeOnEdit { get; init; }
    /// <summary>编辑流程可省略保存范围提示页，确认页仍展示实际保存位置。</summary>
    public bool OmitSaveLocationOnEdit { get; init; }
}
