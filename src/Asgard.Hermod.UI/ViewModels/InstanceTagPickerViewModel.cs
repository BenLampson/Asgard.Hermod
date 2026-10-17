namespace Asgard.Hermod.UI.ViewModels;

/// <summary>实例关联标签的独立弹出草稿，取消和搜索不会改变已确认的选择。</summary>
public sealed class InstanceTagPickerViewModel : ObservableObject
{
    private readonly InstanceTagOptionViewModel[] _options;
    private readonly ObservableCollection<InstanceTagOptionViewModel> _rows = [];
    private bool _initializing = true;

    /// <summary>按稳定标识还原独立选择；目录中暂缺的已有标识仍保留为可移除项。</summary>
    internal InstanceTagPickerViewModel(IReadOnlyList<ManagedTag> tags, IReadOnlyList<string> selectedIds)
    {
        var ids = selectedIds.ToHashSet(StringComparer.Ordinal);
        var catalog = tags.Concat(ids.Except(tags.Select(tag => tag.Id), StringComparer.Ordinal)
            .Select(id => new ManagedTag(id, Text.Get("InstanceTagUnavailable"), null, "#8791A2", false,
                Text.Get("InstanceTagUnavailable")))).ToArray();
        _options = catalog.Select(tag => new InstanceTagOptionViewModel(tag, Refresh)
        { IsSelected = ids.Contains(tag.Id) }).ToArray();
        var index = _options.ToDictionary(option => option.Tag.Id, StringComparer.Ordinal);
        foreach (var option in _options)
        {
            if (option.Tag.ParentId is not null && index.TryGetValue(option.Tag.ParentId, out var parent))
            {
                // 异常目录不能生成环或无限缩进，真实目录通常由持久化层保证无环。
                var seen = new HashSet<string>(StringComparer.Ordinal) { option.Tag.Id };
                for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Tag.ParentId is { } id
                    ? index.GetValueOrDefault(id) : null)
                    if (!seen.Add(ancestor.Tag.Id)) throw Text.Exception("InstanceTagHierarchyInvalid");
                option.Parent = parent;
                parent.Children.Add(option);
                option.Depth = seen.Count - 1;
            }
        }
        Rows = new(_rows);
        ApplyCommand = new RelayCommand(_ => Complete(true), _ => !IsCompleted);
        CancelCommand = new RelayCommand(_ => Complete(false), _ => !IsCompleted);
        ClearCommand = new RelayCommand(_ => ClearSelection(), _ => !IsCompleted);
        _initializing = false;
        Refresh();
    }

    /// <summary>展开与搜索后的可见项，选择状态始终保留在完整目录。</summary>
    public ReadOnlyObservableCollection<InstanceTagOptionViewModel> Rows { get; }
    /// <summary>提交稳定标识，不提交路径或译文。</summary>
    public IReadOnlyList<string> SelectedIds => _options.Where(option => option.IsSelected).Select(option => option.Tag.Id).ToArray();
    /// <summary>搜索匹配名称与完整路径，包括系统标签译文。</summary>
    public string SearchText
    {
        get;
        set { if (SetProperty(ref field, value ?? "")) Refresh(); }
    } = "";
    /// <summary>完成后命令不能再次触发宿主修改。</summary>
    public bool IsCompleted { get; private set; }
    /// <summary>弹出面板底部显示实际选择数量。</summary>
    public string SelectionSummary => Text.Format("InstanceTagsSelected", SelectedIds.Count);
    /// <summary>区分空目录与搜索无匹配结果。</summary>
    public string EmptyMessage => Text.Get(_options.Length == 0 ? "InstanceTagsEmpty" : "NoMatchingTags");
    /// <summary>确认本次草稿，不直接保存实例。</summary>
    public RelayCommand ApplyCommand { get; }
    /// <summary>取消本次草稿，宿主保留原选择。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>仅清空弹出草稿，确认后才改变宿主。</summary>
    public RelayCommand ClearCommand { get; }
    /// <summary>只发送一次完成事件，true 表示确认选择。</summary>
    public event EventHandler<bool>? Completed;

    /// <summary>批量清空只重建一次列表，避免大量标签时反复刷新。</summary>
    private void ClearSelection()
    {
        _initializing = true;
        foreach (var option in _options) option.IsSelected = false;
        _initializing = false;
        Refresh();
    }

    /// <summary>搜索保留匹配项的祖先，正常浏览尊重独立展开状态。</summary>
    private void Refresh()
    {
        if (_initializing) return;
        string query = SearchText.Trim();
        var retained = new HashSet<InstanceTagOptionViewModel>();
        if (query.Length > 0)
            foreach (var option in _options.Where(option => option.DisplayPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                || option.Tag.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
                || option.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
                for (var ancestor = option; ancestor is not null; ancestor = ancestor.Parent) retained.Add(ancestor);
        _rows.Clear();
        foreach (var root in _options.Where(option => option.Parent is null)) Append(root);
        OnPropertyChanged(nameof(SelectionSummary));

        void Append(InstanceTagOptionViewModel option)
        {
            if (query.Length > 0 && !retained.Contains(option)) return;
            _rows.Add(option);
            if (query.Length > 0 || option.IsExpanded)
                foreach (var child in option.Children) Append(child);
        }
    }

    /// <summary>完成后冻结操作，防止重复点击提交同一草稿。</summary>
    private void Complete(bool apply)
    {
        if (IsCompleted) return;
        IsCompleted = true;
        ApplyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        Completed?.Invoke(this, apply);
    }
}
