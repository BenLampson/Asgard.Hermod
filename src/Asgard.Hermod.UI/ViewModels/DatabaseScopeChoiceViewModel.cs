namespace Asgard.Hermod.UI.ViewModels;

/// <summary>范围编辑的本地草稿，勾选不会直接改变 Session。</summary>
public sealed class DatabaseScopeChoiceViewModel(DatabaseIdentity identity, bool included) : ObservableObject
{
    /// <summary>稳定数据库身份。</summary>
    public DatabaseIdentity Identity { get; } = identity;
    /// <summary>原始名称不翻译。</summary>
    public string Name => Identity.Name;
    /// <summary>编辑开始时已经纳入范围。</summary>
    public bool IsIncluded { get; } = included;
    /// <summary>仅本地选择，支持明确撤销。</summary>
    public bool IsSelected
    {
        get; set
        {
            if (!SetProperty(ref field, value)) return;
            foreach (string name in new[] { nameof(State), nameof(IsPendingAdd), nameof(IsPendingRemove) }) OnPropertyChanged(name);
        }
    } = included;
    /// <summary>新增状态只由稳定布尔值判断。</summary>
    public bool IsPendingAdd => IsSelected && !IsIncluded;
    /// <summary>移除状态只由稳定布尔值判断。</summary>
    public bool IsPendingRemove => !IsSelected && IsIncluded;
    /// <summary>状态使用完整翻译资源。</summary>
    public string State => Text.Get(IsPendingAdd ? "DatabaseScopePendingAdd" : IsPendingRemove ? "DatabaseScopePendingRemove" : IsIncluded ? "DatabaseScopeIncluded" : "DatabaseScopeAvailable");
}
