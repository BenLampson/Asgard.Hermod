namespace Asgard.Hermod.UI.Models;

/// <summary>供导航显示的实例快照；凭据与连接实现由应用层持有。</summary>
public sealed record InstanceSummary(
    string Id, string Name, string DisplayName, string Icon,
    IReadOnlyList<string> Tags, bool IsLocal = false, bool HasAlert = false, string Endpoint = "");
