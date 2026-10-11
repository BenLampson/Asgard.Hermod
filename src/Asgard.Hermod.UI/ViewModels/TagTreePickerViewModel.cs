namespace Asgard.Hermod.UI.ViewModels;

/// <summary>标签树筛选的独立草稿；提交和取消由宿主决定如何应用。</summary>
public sealed class TagTreePickerViewModel : ObservableObject
{
    private readonly ObservableCollection<TagTreeNodeViewModel> _rows = [];
    private readonly ObservableCollection<string> _selectedPaths = [];
    private readonly Dictionary<string, TagTreeNodeViewModel> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _unresolvedPaths = [];
    private readonly string[] _initialPaths;
    private readonly TagFilterMatchMode _initialMode;
    private string _searchText = "";
    private TagFilterMatchMode _matchMode;
    private bool _isCompleted;

    /// <summary>复制打开时的选择，后续勾选不会提前修改宿主筛选。</summary>
    public TagTreePickerViewModel(IReadOnlyList<TagFilterDefinition> roots, IReadOnlyList<string> selectedPaths,
        TagFilterMatchMode mode)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(selectedPaths);
        ValidateMode(mode);
        _initialPaths = selectedPaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _initialMode = _matchMode = mode;
        Roots = Array.AsReadOnly(roots.Select(root => new TagTreeNodeViewModel(root, null, 0,
            SelectBranch, () => RebuildRows(false))).ToArray());
        foreach (TagTreeNodeViewModel node in EnumerateNodes(Roots))
        {
            if (!_nodes.TryAdd(node.Path, node))
                throw new ArgumentException($"标签路径重复：{node.Path}", nameof(roots));
        }
        Rows = new ReadOnlyObservableCollection<TagTreeNodeViewModel>(_rows);
        SelectedPaths = new ReadOnlyObservableCollection<string>(_selectedPaths);
        ApplyCommand = new RelayCommand(_ => Complete(true), _ => !IsCompleted);
        CancelCommand = new RelayCommand(_ => Complete(false), _ => !IsCompleted);
        ClearCommand = new RelayCommand(_ => ClearSelection(), _ => !IsCompleted);
        RemoveSelectedCommand = new RelayCommand(path => RemoveSelection(path as string), _ => !IsCompleted);
        RestoreSelection(_initialPaths);
        // 按结构展开设计中的首两组和首组第一条分支，不依赖示例标签名称。
        foreach (TagTreeNodeViewModel root in Roots.Where(root => root.HasChildren).Take(2))
            root.IsExpanded = true;
        TagTreeNodeViewModel? firstBranch = Roots.FirstOrDefault(root => root.HasChildren)?
            .Children.FirstOrDefault(child => child.HasChildren);
        if (firstBranch is not null)
            firstBranch.IsExpanded = true;
        // 已有选择的祖先始终展开，保证打开时能看到当前筛选路径。
        foreach (string path in _initialPaths)
            if (_nodes.TryGetValue(path, out TagTreeNodeViewModel? node))
                for (TagTreeNodeViewModel? parent = node.Parent; parent is not null; parent = parent.Parent)
                    parent.IsExpanded = true;
        RebuildRows(true);
    }

    /// <summary>全部根分组，展开与搜索均不移除源节点。</summary>
    public IReadOnlyList<TagTreeNodeViewModel> Roots { get; }

    /// <summary>当前展开并符合搜索条件的可见行。</summary>
    public ReadOnlyObservableCollection<TagTreeNodeViewModel> Rows { get; }

    /// <summary>选中草稿；全选分支压缩为最高层路径，半选祖先不进入结果。</summary>
    public ReadOnlyObservableCollection<string> SelectedPaths { get; }

    /// <summary>按名称或完整路径搜索，保留命中标签的祖先。</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
                RebuildRows(true);
        }
    }

    /// <summary>选中路径之间的 ANY 或 ALL 条件，仅在提交时影响宿主。</summary>
    public TagFilterMatchMode MatchMode
    {
        get => _matchMode;
        set
        {
            ValidateMode(value);
            SetProperty(ref _matchMode, value);
        }
    }

    /// <summary>实际树中的标签总数，包含根分组。</summary>
    public int TotalTagCount => _nodes.Count;

    /// <summary>实际根分组数量。</summary>
    public int GroupCount => Roots.Count;

    /// <summary>摘要中的选中路径数量。</summary>
    public int SelectedCount => SelectedPaths.Count;

    /// <summary>提交或取消后不再重复发送完成事件。</summary>
    public bool IsCompleted => _isCompleted;

    /// <summary>选择摘要使用设计文字并展示实际数量。</summary>
    public string SelectionSummary => $"{SelectedCount} selected · includes nested tags";

    /// <summary>应用按钮使用实际路径数量。</summary>
    public string ApplyLabel => $"Apply filters ({SelectedCount})";

    /// <summary>树导航提示使用实际分组数量。</summary>
    public string GroupSummary => $"{GroupCount} groups · expand to browse";

    /// <summary>向宿主交付当前选择和匹配模式。</summary>
    public RelayCommand ApplyCommand { get; }

    /// <summary>恢复打开前的草稿并通知宿主关闭。</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>仅清空当前草稿。</summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>按命令参数中的路径移除选中分支。</summary>
    public RelayCommand RemoveSelectedCommand { get; }

    /// <summary>true 表示点击应用，false 表示取消；外部只在 true 时读取结果。</summary>
    public event EventHandler<bool>? Completed;

    /// <summary>将整枝设为同一选择状态，再逐级汇总祖先。</summary>
    private void SelectBranch(TagTreeNodeViewModel node, bool selected)
    {
        if (IsCompleted)
            return;
        SetBranchState(node, selected);
        for (TagTreeNodeViewModel? parent = node.Parent; parent is not null; parent = parent.Parent)
            RefreshParent(parent);
        RefreshSelectedPaths();
    }

    /// <summary>递归设置子节点；搜索隐藏的后代仍参与级联选择。</summary>
    private static void SetBranchState(TagTreeNodeViewModel node, bool selected)
    {
        node.SetCheckedState(selected);
        foreach (TagTreeNodeViewModel child in node.Children)
            SetBranchState(child, selected);
    }

    /// <summary>根据直接子标签计算父标签的全选、未选或半选。</summary>
    private static void RefreshParent(TagTreeNodeViewModel node)
    {
        node.SetCheckedState(node.Children.All(child => child.IsChecked == true) ? true
            : node.Children.All(child => child.IsChecked == false) ? false : null);
    }

    /// <summary>从最高全选分支生成紧凑路径，避免 ALL 同时要求同枝全部叶标签。</summary>
    private void RefreshSelectedPaths()
    {
        _selectedPaths.Clear();
        foreach (TagTreeNodeViewModel root in Roots)
            AppendSelection(root);
        foreach (string path in _unresolvedPaths)
            _selectedPaths.Add(path);
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(ApplyLabel));
    }

    /// <summary>遇到全选节点即停止向下展开摘要。</summary>
    private void AppendSelection(TagTreeNodeViewModel node)
    {
        if (node.IsChecked == true)
            _selectedPaths.Add(node.Path);
        else
            foreach (TagTreeNodeViewModel child in node.Children)
                AppendSelection(child);
    }

    /// <summary>按真实树还原勾选，保留暂时不在目录中的已有路径以免静默丢失条件。</summary>
    private void RestoreSelection(IEnumerable<string> paths)
    {
        foreach (TagTreeNodeViewModel root in Roots)
            SetBranchState(root, false);
        _unresolvedPaths.Clear();
        foreach (string path in paths)
        {
            if (_nodes.TryGetValue(path, out TagTreeNodeViewModel? node))
            {
                SetBranchState(node, true);
                for (TagTreeNodeViewModel? parent = node.Parent; parent is not null; parent = parent.Parent)
                    RefreshParent(parent);
            }
            else
                _unresolvedPaths.Add(path);
        }
        RefreshSelectedPaths();
    }

    /// <summary>移除芯片代表的整枝，未知路径也可以直接删除。</summary>
    private void RemoveSelection(string? path)
    {
        if (path is null)
            return;
        if (_nodes.TryGetValue(path, out TagTreeNodeViewModel? node))
            SelectBranch(node, false);
        else
        {
            _unresolvedPaths.RemoveAll(item => StringComparer.OrdinalIgnoreCase.Equals(item, path));
            RefreshSelectedPaths();
        }
    }

    /// <summary>清空草稿，外部实例列表在应用前保持原筛选。</summary>
    private void ClearSelection() => RestoreSelection([]);

    /// <summary>取消还原初始状态，完成事件始终最多发送一次。</summary>
    private void Complete(bool apply)
    {
        if (!apply)
        {
            RestoreSelection(_initialPaths);
            MatchMode = _initialMode;
        }
        _isCompleted = true;
        OnPropertyChanged(nameof(IsCompleted));
        ApplyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        Completed?.Invoke(this, apply);
    }

    /// <summary>搜索使用保留集合，不改变标签树和勾选结果。</summary>
    private void RebuildRows(bool resetSearchExpansion)
    {
        string query = SearchText.Trim();
        HashSet<TagTreeNodeViewModel>? retained = null;
        if (query.Length > 0)
        {
            retained = [];
            foreach (TagTreeNodeViewModel root in Roots)
                RetainMatches(root, query, retained);
        }
        foreach (TagTreeNodeViewModel node in _nodes.Values)
            node.SetSearchState(retained is not null, resetSearchExpansion);
        _rows.Clear();
        foreach (TagTreeNodeViewModel root in Roots)
            AppendRows(root, retained);
    }

    /// <summary>逐级保留命中名称或路径的标签及其祖先。</summary>
    private static bool RetainMatches(TagTreeNodeViewModel node, string query,
        HashSet<TagTreeNodeViewModel> retained)
    {
        bool matches = node.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || node.Path.Contains(query, StringComparison.OrdinalIgnoreCase);
        foreach (TagTreeNodeViewModel child in node.Children)
            matches |= RetainMatches(child, query, retained);
        if (matches)
            retained.Add(node);
        return matches;
    }

    /// <summary>按当前展开状态生成可见行，叶标签不产生空的展开区域。</summary>
    private void AppendRows(TagTreeNodeViewModel node, HashSet<TagTreeNodeViewModel>? retained)
    {
        if (retained is not null && !retained.Contains(node))
            return;
        _rows.Add(node);
        if (node.IsEffectivelyExpanded)
            foreach (TagTreeNodeViewModel child in node.Children)
                AppendRows(child, retained);
    }

    /// <summary>遍历全部源节点，供目录索引与统计使用。</summary>
    private static IEnumerable<TagTreeNodeViewModel> EnumerateNodes(IEnumerable<TagTreeNodeViewModel> nodes)
    {
        foreach (TagTreeNodeViewModel node in nodes)
        {
            yield return node;
            foreach (TagTreeNodeViewModel child in EnumerateNodes(node.Children))
                yield return child;
        }
    }

    /// <summary>公开模式设置不接受未定义的枚举值。</summary>
    private static void ValidateMode(TagFilterMatchMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "标签匹配模式不存在。");
    }
}
