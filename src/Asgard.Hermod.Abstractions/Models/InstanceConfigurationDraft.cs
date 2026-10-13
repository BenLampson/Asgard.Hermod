namespace Asgard.Hermod.Abstractions.Models;

/// <summary>提交实例配置时使用的草稿，凭据仅交给配置服务处理。</summary>
public sealed class InstanceConfigurationDraft
{
    /// <summary>实例显示名称。</summary>
    public required string Name { get; init; }
    /// <summary>服务商专属连接参数。</summary>
    public required InstanceConnectionOptions Connection { get; init; }
    /// <summary>由登录状态决定的保存范围，客户端不能指定任意账户归属。</summary>
    public InstanceSaveLocation SaveLocation { get; init; } = InstanceSaveLocation.Local;
    /// <summary>编辑器打开时的账户边界，防止等待保存期间切换账户造成配置串入。</summary>
    public HermodAccount? ExpectedAccount { get; init; }
    /// <summary>区分未登录边界与未指定边界的兼容调用。</summary>
    public bool CheckExpectedAccount { get; init; }
    /// <summary>稳定的标签标识，改名或调整层级不会改变关联。</summary>
    public IReadOnlyList<string> TagIds { get; init; } = [];
    /// <summary>编辑时明确替换标签关系；兼容只更新连接信息的旧调用方。</summary>
    public bool ReplaceTags { get; init; }

    /// <summary>避免草稿诊断输出包含连接凭据。</summary>
    public override string ToString() => $"Instance draft ({SaveLocation}, connection hidden)";
}
