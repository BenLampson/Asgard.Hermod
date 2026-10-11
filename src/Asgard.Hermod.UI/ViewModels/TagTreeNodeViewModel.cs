namespace Asgard.Hermod.UI.ViewModels;

/// <summary>标签树中的展开与三态勾选状态，级联操作交给所属筛选草稿。</summary>
public sealed class TagTreeNodeViewModel : ObservableObject
{
    private readonly Action<TagTreeNodeViewModel, bool> _select;
    private readonly Action _expand;
    private bool? _isChecked = false;
    private bool _isExpanded;
    private bool _isSearching;
    private bool _searchExpanded = true;

    /// <summary>按定义递归建立节点和父级关联，不复制外部可变选择状态。</summary>
    internal TagTreeNodeViewModel(TagFilterDefinition definition, TagTreeNodeViewModel? parent, int depth,
        Action<TagTreeNodeViewModel, bool> select, Action expand)
    {
        Name = definition.Name;
        Path = definition.Path;
        Count = definition.Count;
        Color = definition.Color;
        Parent = parent;
        Depth = depth;
        _select = select;
        _expand = expand;
        Children = Array.AsReadOnly(definition.Children
            .Select(child => new TagTreeNodeViewModel(child, this, depth + 1, select, expand)).ToArray());
        ToggleCheckCommand = new RelayCommand(_ => _select(this, IsChecked != true));
        ToggleExpansionCommand = new RelayCommand(_ => ToggleExpansion(), _ => HasChildren);
    }

    /// <summary>当前标签名称。</summary>
    public string Name { get; }

    /// <summary>选中摘要和实际筛选使用的完整路径。</summary>
    public string Path { get; }

    /// <summary>实际后代数量，叶标签为零。</summary>
    public int Count { get; }
    /// <summary>管理窗口中定义的颜色，同一目录也用于筛选器。</summary>
    public string? Color { get; }
    /// <summary>虚拟分组没有颜色，不占用色点的位置。</summary>
    public bool HasColor => !string.IsNullOrWhiteSpace(Color);

    /// <summary>保持定义顺序的直接子节点。</summary>
    public IReadOnlyList<TagTreeNodeViewModel> Children { get; }

    /// <summary>所属的直接父标签，根分组为空。</summary>
    public TagTreeNodeViewModel? Parent { get; }

    /// <summary>用于每层十八像素缩进的层级。</summary>
    public int Depth { get; }

    /// <summary>根分组以半粗体呈现。</summary>
    public bool IsRoot => Parent is null;

    /// <summary>叶标签保留箭头占位，但不提供展开操作。</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>标签行的水平内边距，右侧保持十像素。</summary>
    public Thickness RowPadding => new(10 + Depth * 18, 0, 10, 0);

    /// <summary>未选、全选、半选；写入时级联选择整枝并更新祖先。</summary>
    public bool? IsChecked
    {
        get => _isChecked;
        set => _select(this, value == true);
    }

    /// <summary>正常浏览时的展开状态，搜索不会覆盖此状态。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value))
                return;
            NotifyExpansion();
            _expand();
        }
    }

    /// <summary>搜索时单独展开命中路径；退出搜索后恢复原有展开状态。</summary>
    public bool IsEffectivelyExpanded => _isSearching ? _searchExpanded : IsExpanded;

    /// <summary>直接使用官方 Lucide 的展开或折叠箭头。</summary>
    public string ExpansionIcon => IsEffectivelyExpanded ? "chevron-down" : "chevron-right";

    /// <summary>半选节点点击后选择整枝，全选节点点击后取消整枝。</summary>
    public RelayCommand ToggleCheckCommand { get; }

    /// <summary>展开或折叠当前可浏览的子标签。</summary>
    public RelayCommand ToggleExpansionCommand { get; }

    /// <summary>由所属草稿更新三态，避免递归触发外部设置器。</summary>
    internal void SetCheckedState(bool? value) => SetProperty(ref _isChecked, value, nameof(IsChecked));

    /// <summary>查询变更时展开匹配分支，同一查询内允许手动折叠。</summary>
    internal void SetSearchState(bool searching, bool resetExpansion)
    {
        _isSearching = searching;
        if (resetExpansion)
            _searchExpanded = true;
        NotifyExpansion();
    }

    /// <summary>搜索中的展开操作只改变临时浏览状态。</summary>
    private void ToggleExpansion()
    {
        if (!_isSearching)
        {
            IsExpanded = !IsExpanded;
            return;
        }
        _searchExpanded = !_searchExpanded;
        NotifyExpansion();
        _expand();
    }

    /// <summary>使箭头与实际展开状态同步刷新。</summary>
    private void NotifyExpansion()
    {
        OnPropertyChanged(nameof(IsEffectivelyExpanded));
        OnPropertyChanged(nameof(ExpansionIcon));
    }
}
