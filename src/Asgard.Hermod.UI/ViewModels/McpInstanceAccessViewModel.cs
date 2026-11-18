namespace Asgard.Hermod.UI.ViewModels;

/// <summary>默认允许的实例勾选项；取消勾选时立即保存禁止规则。</summary>
public sealed class McpInstanceAccessViewModel : ObservableObject
{
    private readonly Action<McpInstanceAccessViewModel, bool> _changed;
    private bool _allowed;
    /// <summary>实例名称仅用于显示，控制逻辑使用稳定标识。</summary>
    public McpInstanceAccessViewModel(McpSettingsTarget target, bool allowed, Action<McpInstanceAccessViewModel, bool> changed)
    { Target = target; _allowed = allowed; _changed = changed; }
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
    /// <summary>保存失败时恢复上一次实际策略，不触发新的写入。</summary>
    internal void Restore(bool allowed) => SetProperty(ref _allowed, allowed, nameof(IsAllowed));
}
