namespace Asgard.Hermod.UI.ViewModels;

/// <summary>一行已保存的实例权限；菜单选择成功保存后才更新显示状态。</summary>
public sealed class McpInstanceAccessViewModel(McpSettingsTarget target, string name, string typeName, McpInstancePolicy policy,
    Func<McpInstanceAccessViewModel, McpInstancePolicy, Task> change) : ObservableObject
{
    /// <summary>使用稳定身份和连接版本处理更改。</summary>
    public McpSettingsTarget Target => target;
    /// <summary>重名时附带短标识的实例名称。</summary>
    public string Name => name;
    /// <summary>本地化的协议类型名称。</summary>
    public string TypeName => typeName;
    /// <summary>实际生效的互斥策略。</summary>
    public McpInstancePolicy Policy { get; private set; } = policy;
    /// <summary>菜单按钮显示的策略名称。</summary>
    public string PolicyLabel => McpPolicyFilter.LabelFor(Policy);
    /// <summary>按钮以图标和文字同时表达当前策略。</summary>
    public string PolicyIcon => Policy switch { McpInstancePolicy.AlwaysAllow => "shield-check", McpInstancePolicy.Blocked => "circle-x", _ => "message-square" };
    /// <summary>辅助技术同时读出实例与当前策略。</summary>
    public string AccessibleLabel => Text.Format("McpPolicyForInstance", target.Label, PolicyLabel);
    /// <summary>实例类型对应的内置图标。</summary>
    public string Icon => target.Target.TypeId switch
    {
        "storage" => "box",
        "ssh" => "terminal",
        "redis" => "layers",
        "rabbitmq" => "network",
        _ => "database",
    };
    /// <summary>用户显式选择后才写入权限，打开菜单不会更改权限。</summary>
    public AsyncRelayCommand SetPolicyCommand => field ??= new(value => change(this, (McpInstancePolicy)value!),
        value => value is McpInstancePolicy);

    /// <summary>保存完成后更新原行，保留可见项的容器和键盘焦点。</summary>
    internal void ApplyPolicy(McpInstancePolicy value)
    {
        if (Policy == value) return;
        Policy = value;
        OnPropertyChanged(nameof(Policy)); OnPropertyChanged(nameof(PolicyLabel));
        OnPropertyChanged(nameof(PolicyIcon)); OnPropertyChanged(nameof(AccessibleLabel));
    }
}
