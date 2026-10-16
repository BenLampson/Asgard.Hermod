namespace Asgard.Hermod.UI.ViewModels;

/// <summary>统一范围浮层的草稿；只有应用动作才进入模块的确认与连接保护。</summary>
public sealed class DatabaseScopeEditorViewModel : ObservableObject
{
    private readonly DatabaseSessionScope _scope;
    private readonly long _revision;
    private readonly Func<DatabaseScopeChange, Task<bool>> _apply;

    /// <summary>候选必须来自同一实例；同时保留暂时不在目录中的已有成员。</summary>
    public DatabaseScopeEditorViewModel(DatabaseSessionScope scope, IEnumerable<DatabaseIdentity> candidates, Func<DatabaseScopeChange, Task<bool>> apply)
    {
        _scope = scope; _revision = scope.Revision; _apply = apply;
        var identities = scope.Databases.Concat(candidates).ToArray();
        if (identities.Any(item => item.InstanceId != scope.InstanceId)) throw Text.Exception("DatabaseSessionInstanceMismatch");
        Choices = identities.DistinctBy(item => item.Name, StringComparer.Ordinal).Select(item => new DatabaseScopeChoiceViewModel(item, scope.Contains(item.Name))).ToArray();
        ApplyCommand = new AsyncRelayCommand(_ => ApplyAsync(), _ => !IsApplying && IsCurrent && HasChanges && Choices.Any(item => item.IsSelected));
        CancelCommand = new RelayCommand(_ => IsOpen = false, _ => !IsApplying);
        foreach (var item in Choices) item.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(item.IsSelected)) Refresh(); };
    }

    /// <summary>开始编辑时的真实候选顺序。</summary>
    public IReadOnlyList<DatabaseScopeChoiceViewModel> Choices { get; }
    /// <summary>范围被外部改变后拒绝迟到草稿。</summary>
    public bool IsCurrent => _scope.Revision == _revision;
    /// <summary>用户实际改变过选择才允许应用。</summary>
    public bool HasChanges => Choices.Any(item => item.IsSelected != item.IsIncluded);
    /// <summary>展示勾选数量，不能用翻译标签驱动业务。</summary>
    public string SelectionSummary => Text.Format("DatabaseSessionSelectedCount", Choices.Count(item => item.IsSelected));
    /// <summary>应用期间保持取消和重复提交禁用。</summary>
    public bool IsApplying { get; private set { if (SetProperty(ref field, value)) Refresh(); } }
    /// <summary>应用期间不能再修改已冻结的选择。</summary>
    public bool CanEdit => !IsApplying;
    /// <summary>浮层的显式打开状态。</summary>
    public bool IsOpen { get; set => SetProperty(ref field, value); } = true;
    /// <summary>统一提交新增与移除。</summary>
    public AsyncRelayCommand ApplyCommand { get; }
    /// <summary>取消丢弃草稿，不改变范围。</summary>
    public RelayCommand CancelCommand { get; }

    private async Task ApplyAsync()
    {
        if (!IsCurrent || !HasChanges) return;
        var change = _scope.PrepareChange(Choices.Where(item => item.IsSelected).Select(item => item.Identity));
        IsApplying = true;
        try { if (await _apply(change)) IsOpen = false; }
        finally { IsApplying = false; }
    }
    private void Refresh() { OnPropertyChanged(nameof(SelectionSummary)); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(IsCurrent)); OnPropertyChanged(nameof(CanEdit)); ApplyCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); }
}
