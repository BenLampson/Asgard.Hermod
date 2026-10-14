namespace Asgard.Hermod.Abstractions.Models;

/// <summary>只修改实例显示元数据；空值表示保留，空标签集合表示清空关联。</summary>
public sealed record InstanceMetadataChange
{
    /// <summary>新名称；null 保留当前名称。</summary>
    public string? Name { get; init; }
    /// <summary>替换稳定标签关联；null 保留当前关联。</summary>
    public IReadOnlyList<string>? TagIds { get; init; }
    /// <summary>打开轻量窗口时的账户边界。</summary>
    public HermodAccount? ExpectedAccount { get; init; }
    /// <summary>区分未登录边界与兼容调用未指定账户边界。</summary>
    public bool CheckExpectedAccount { get; init; }
}
