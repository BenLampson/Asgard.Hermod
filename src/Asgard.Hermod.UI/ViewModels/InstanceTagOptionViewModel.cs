namespace Asgard.Hermod.UI.ViewModels;

/// <summary>实例标签草稿中的独立复选项，父子关系只用于浏览，不级联勾选。</summary>
public sealed class InstanceTagOptionViewModel : ObservableObject
{
    private readonly Action _changed;

    /// <summary>保留稳定标签标识和完整路径，供保存和无歧义显示使用。</summary>
    internal InstanceTagOptionViewModel(ManagedTag tag, Action changed)
    {
        Tag = tag;
        _changed = changed;
        ToggleExpansionCommand = new RelayCommand(_ => IsExpanded = !IsExpanded, _ => HasChildren);
    }

    /// <summary>标签原始快照，显示文字不参与业务身份判断。</summary>
    public ManagedTag Tag { get; }
    /// <summary>系统标签按界面语言显示，自定义路径保持原文。</summary>
    public string DisplayPath => Tag.IsBuiltIn ? Localization.TagDisplayText.FullPath(Tag.Path) : Tag.Path;
    /// <summary>系统标签名称本地化，自定义名称保持原文。</summary>
    public string DisplayName => Tag.IsBuiltIn ? Localization.TagDisplayText.Name(Tag.Path, Tag.Name) : Tag.Name;
    /// <summary>实际父节点，用于搜索时保留祖先。</summary>
    internal InstanceTagOptionViewModel? Parent { get; set; }
    /// <summary>层级缩进，不使用显示路径分隔符计算身份。</summary>
    public Thickness RowPadding => new(10 + Depth * 18, 0, 10, 0);
    /// <summary>目录树中的深度。</summary>
    internal int Depth { get; set; }
    /// <summary>实际子项，只用于展开浏览。</summary>
    internal List<InstanceTagOptionViewModel> Children { get; } = [];
    /// <summary>叶节点不显示展开按钮。</summary>
    public bool HasChildren => Children.Count > 0;
    /// <summary>独立勾选当前标签，不修改父标签或子标签。</summary>
    public bool IsSelected
    {
        get;
        set { if (SetProperty(ref field, value)) _changed(); }
    }
    /// <summary>正常浏览的展开状态；搜索不会覆盖此值。</summary>
    public bool IsExpanded
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            OnPropertyChanged(nameof(ExpansionIcon));
            _changed();
        }
    } = true;
    /// <summary>使用统一图标组件显示展开状态。</summary>
    public string ExpansionIcon => IsExpanded ? "chevron-down" : "chevron-right";
    /// <summary>折叠与展开只影响列表展示。</summary>
    public RelayCommand ToggleExpansionCommand { get; }
}
