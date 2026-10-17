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
    /// <summary>同一类型切换连接方式时是否沿用共同字段；不同云服务商默认清空配置，避免误用凭据。</summary>
    public bool PreserveFieldsOnProviderChange { get; init; }
    /// <summary>编辑流程可省略保存范围提示页，确认页仍展示实际保存位置。</summary>
    public bool OmitSaveLocationOnEdit { get; init; }
    /// <summary>首步默认选项必须使用注册表中的稳定标识，仍需用户点击下一步进入表单。</summary>
    public string? DefaultProviderId { get; init; }
    /// <summary>自动保存范围无需独立提示页时，仍在确认页显示真实位置。</summary>
    public bool OmitSaveLocation { get; init; }
}
