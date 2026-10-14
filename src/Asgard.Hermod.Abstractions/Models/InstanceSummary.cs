namespace Asgard.Hermod.Abstractions.Models;

/// <summary>供导航显示的实例快照；凭据与连接实现由应用层持有。</summary>
public sealed record InstanceSummary(
    string Id, string Name, string DisplayName, string Icon,
    IReadOnlyList<string> Tags, bool IsLocal = false, bool HasAlert = false, string Endpoint = "",
    string TypeId = "", string ProviderId = "")
{
    /// <summary>稳定标签标识用于环境辨识；显示路径不能作为环境身份。</summary>
    public IReadOnlyList<string> TagIds { get; init; } = [];
    /// <summary>持久化配置身份已存在；不代表此刻仍能访问远程服务。</summary>
    public bool HasConfiguration => !string.IsNullOrWhiteSpace(TypeId) && !string.IsNullOrWhiteSpace(ProviderId);
}
