namespace Asgard.Hermod.UI.ViewModels;

/// <summary>默认允许的实例勾选项；取消勾选时立即保存禁止规则。</summary>
public sealed class McpInstanceAccessViewModel : ObservableObject
{
    private readonly Action<McpInstanceAccessViewModel, bool> _changed;
    private readonly Action<McpInstanceAccessViewModel, bool>? _approvalChanged;
    private bool _allowed, _alwaysAllow;
    /// <summary>实例名称仅用于显示，控制逻辑使用稳定标识。</summary>
    public McpInstanceAccessViewModel(McpSettingsTarget target, bool allowed, Action<McpInstanceAccessViewModel, bool> changed,
        bool alwaysAllow = false, Action<McpInstanceAccessViewModel, bool>? approvalChanged = null)
    { Target = target; _allowed = allowed; _changed = changed; _alwaysAllow = alwaysAllow; _approvalChanged = approvalChanged; }
    /// <summary>当前实例的稳定身份。</summary>
    public McpSettingsTarget Target { get; }
    /// <summary>实例及类型的本地化名称。</summary>
    public string Label => Target.Label;
    /// <summary>取消勾选禁止整个实例；再次勾选不清除其更细的禁止范围。</summary>
    public bool IsAllowed
    {
        get => _allowed;
        set { if (SetProperty(ref _allowed, value)) _changed(this, value); }
    }
    /// <summary>勾选后本机已配对客户端可直接访问此连接版本允许的资源。</summary>
    public bool AlwaysAllow
    {
        get => _alwaysAllow;
        set { if (SetProperty(ref _alwaysAllow, value)) _approvalChanged?.Invoke(this, value); }
    }
    /// <summary>保存失败时恢复上一次实际策略，不触发新的写入。</summary>
    internal void Restore(bool allowed) => SetProperty(ref _allowed, allowed, nameof(IsAllowed));
    internal void RestoreApproval(bool enabled) => SetProperty(ref _alwaysAllow, enabled, nameof(AlwaysAllow));
}
