namespace Asgard.Hermod.UI.ViewModels;

/// <summary>按稳定策略筛选实例，计数来自未过滤的工作区。</summary>
public sealed class McpPolicyFilter(McpInstancePolicy? policy) : ObservableObject
{
    /// <summary>空值对应全部实例。</summary>
    public McpInstancePolicy? Policy => policy;
    /// <summary>本地化的策略名称。</summary>
    public string Label => LabelFor(policy);
    /// <summary>该策略下的实例总数。</summary>
    public int Count { get; internal set => SetProperty(ref field, value); }
    internal static string LabelFor(McpInstancePolicy? policy) => Text.Get(policy switch
    {
        McpInstancePolicy.Confirm => "McpConfirmFirst",
        McpInstancePolicy.AlwaysAllow => "McpAlwaysAllowInstance",
        McpInstancePolicy.Blocked => "McpBlockAccess",
        _ => "McpAllPolicies",
    });
}
