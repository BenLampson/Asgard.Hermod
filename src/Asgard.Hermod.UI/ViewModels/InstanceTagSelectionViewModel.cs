namespace Asgard.Hermod.UI.ViewModels;

/// <summary>确认页持有的标签选择，只在允许编辑的最后一步接受弹出草稿。</summary>
public sealed class InstanceTagSelectionViewModel : ObservableObject
{
    private readonly IReadOnlyList<ManagedTag> _catalog;
    private readonly ObservableCollection<ManagedTag> _selected = [];
    private InstanceTagPickerViewModel? _picker;

    /// <summary>目录和原始稳定标识由编辑服务读取，不从导航显示路径反推。</summary>
    public InstanceTagSelectionViewModel(IReadOnlyList<ManagedTag>? catalog = null, IReadOnlyList<string>? selectedIds = null)
    {
        HasCatalog = catalog is not null;
        _catalog = catalog?.ToArray() ?? [];
        SelectedTags = new(_selected);
        SetSelection(selectedIds ?? []);
        RemoveCommand = new RelayCommand(id => Remove(id as string), _ => IsEditable);
    }

    /// <summary>是否实际读取了目录，旧编辑调用未读取时仍保留已有标签。</summary>
    public bool HasCatalog { get; }
    /// <summary>确认页显示的完整标签路径。</summary>
    public ReadOnlyObservableCollection<ManagedTag> SelectedTags { get; }
    /// <summary>提交实例时的稳定标识快照。</summary>
    public IReadOnlyList<string> SelectedIds => _selected.Select(tag => tag.Id).ToArray();
    /// <summary>入口只展示前两个路径，溢出选择在弹出面板中完整可见。</summary>
    public IEnumerable<InstanceTagOptionViewModel> VisibleTags => _selected.Take(2).Select(tag => new InstanceTagOptionViewModel(tag, () => { }));
    /// <summary>其余标签的折叠数量。</summary>
    public string OverflowLabel => _selected.Count > 2 ? Text.Format("InstanceTagsOverflow", _selected.Count - 2) : "";
    /// <summary>当前选择数量。</summary>
    public string SelectionSummary => Text.Format("InstanceTagsSelected", _selected.Count);
    /// <summary>空选择仍然允许添加实例。</summary>
    public bool HasSelection => _selected.Count > 0;
    /// <summary>只有最后一步且未保存时允许修改。</summary>
    public bool IsEditable { get; private set; }
    /// <summary>未确认草稿打开时阻止向导默认按钮提交实例。</summary>
    public bool HasOpenPicker => _picker is not null;
    /// <summary>入口芯片可按稳定标识单独移除。</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>由向导状态控制编辑，离开确认或开始保存会取消尚未确认的草稿。</summary>
    internal void SetEditable(bool editable)
    {
        IsEditable = editable;
        if (!editable) _picker?.CancelCommand.Execute(null);
        OnPropertyChanged(nameof(IsEditable));
        RemoveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>每次打开复制已确认选择，重复打开先放弃上次草稿。</summary>
    public InstanceTagPickerViewModel? OpenPicker()
    {
        if (!IsEditable) return null;
        _picker?.CancelCommand.Execute(null);
        _picker = new(_catalog, SelectedIds);
        _picker.Completed += OnCompleted;
        OnPropertyChanged(nameof(HasOpenPicker));
        return _picker;
    }

    /// <summary>重新选择实例类型时清空尚未保存的标签。</summary>
    internal void Reset()
    {
        _picker?.CancelCommand.Execute(null);
        SetSelection([]);
    }

    /// <summary>编辑构造完成类型选择后还原服务读取的稳定标识。</summary>
    internal void Restore(IReadOnlyList<string> ids) => SetSelection(ids);

    /// <summary>让宿主优先取消浮层，而不是由 Escape 关闭整个向导。</summary>
    internal bool CancelPicker()
    {
        if (_picker is null) return false;
        _picker.CancelCommand.Execute(null);
        return true;
    }

    /// <summary>只有当前草稿的确认事件能更新选择，取消事件只释放订阅。</summary>
    private void OnCompleted(object? sender, bool applied)
    {
        if (sender is not InstanceTagPickerViewModel picker) return;
        picker.Completed -= OnCompleted;
        if (!ReferenceEquals(picker, _picker)) return;
        if (applied && IsEditable) SetSelection(picker.SelectedIds);
        _picker = null;
        OnPropertyChanged(nameof(HasOpenPicker));
    }

    /// <summary>保留目录中暂缺的标识以免静默丢失旧关联，用户可以清空后重选。</summary>
    private void SetSelection(IEnumerable<string> ids)
    {
        _selected.Clear();
        foreach (string id in ids.Distinct(StringComparer.Ordinal))
            _selected.Add(_catalog.FirstOrDefault(tag => tag.Id == id)
                ?? new(id, Text.Get("InstanceTagUnavailable"), null, "#8791A2", false, Text.Get("InstanceTagUnavailable")));
        foreach (string property in new[] { nameof(SelectedIds), nameof(VisibleTags), nameof(OverflowLabel), nameof(SelectionSummary), nameof(HasSelection) })
            OnPropertyChanged(property);
    }

    /// <summary>芯片移除不改变连接信息或成功测试结果。</summary>
    private void Remove(string? id)
    {
        if (IsEditable && id is not null) SetSelection(SelectedIds.Where(selected => selected != id).ToArray());
    }
}
