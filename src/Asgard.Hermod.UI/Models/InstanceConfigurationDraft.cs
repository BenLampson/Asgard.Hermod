namespace Asgard.Hermod.UI.Models;

/// <summary>提交实例配置时使用的草稿，凭据仅交给配置服务处理。</summary>
public sealed class InstanceConfigurationDraft
{
    /// <summary>实例显示名称。</summary>
    public required string Name { get; init; }
    /// <summary>服务商专属连接参数。</summary>
    public required InstanceConnectionOptions Connection { get; init; }
    /// <summary>由用户明确选择的保存范围。</summary>
    public InstanceSaveLocation SaveLocation { get; init; } = InstanceSaveLocation.Local;
    /// <summary>稳定的标签标识，改名或调整层级不会改变关联。</summary>
    public IReadOnlyList<string> TagIds { get; init; } = [];

    /// <summary>避免草稿诊断输出包含连接凭据。</summary>
    public override string ToString() => $"Instance draft ({SaveLocation}, connection hidden)";
}
