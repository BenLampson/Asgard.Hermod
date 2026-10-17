namespace Asgard.Hermod.UI.ViewModels;

/// <summary>管理树中的自定义标签行，选择与展开不会改变持久化标签。</summary>
public sealed class ManagedTagNodeViewModel : ObservableObject
{
    private bool _isExpanded = true;
    private bool _isSelected;
    private readonly Action _rowsChanged;

    /// <summary>创建只读快照行，窗口操作由管理状态中的服务命令承接。</summary>
    internal ManagedTagNodeViewModel(ManagedTag tag, int depth, TagManagementViewModel owner, Action rowsChanged)
    {
        Tag = tag;
        Depth = depth;
        _rowsChanged = rowsChanged;
        SelectCommand = new RelayCommand(_ => owner.SelectedNode = this);
        ToggleExpandedCommand = new RelayCommand(_ => IsExpanded = !IsExpanded, _ => HasChildren);
        EditCommand = owner.EditCommand;
        RenameCommand = owner.RenameCommand;
        AddChildCommand = owner.AddChildCommand;
        DeleteCommand = owner.DeleteCommand;
    }

    /// <summary>真实来源的标签快照。</summary>
    public ManagedTag Tag { get; }
    /// <summary>稳定标识用于刷新后恢复选择。</summary>
    public string Id => Tag.Id;
    /// <summary>节点名称。</summary>
    public string Name => Tag.Name;
    /// <summary>有效的显示颜色。</summary>
    public string Color => Tag.Color;
    /// <summary>关联实例数量。</summary>
    public int InstanceCount => Tag.InstanceCount;
    /// <summary>树的缩进层数。</summary>
    public int Depth { get; }
    /// <summary>根据层级设置名称区域缩进。</summary>
    public Thickness Indent => new(Depth * 16, 0, 0, 0);
    /// <summary>子级用于保留搜索上下文。</summary>
    public List<ManagedTagNodeViewModel> Children { get; } = [];
    /// <summary>叶子不显示展开操作。</summary>
    public bool HasChildren => Children.Count > 0;
    /// <summary>非搜索状态下用户选择的展开状态。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(ExpansionIcon));
            _rowsChanged();
        }
    }
    /// <summary>折叠与展开图标。</summary>
    public string ExpansionIcon => IsExpanded ? "chevron-down" : "chevron-right";
    /// <summary>右侧正在展示的标签。</summary>
    public bool IsSelected { get => _isSelected; internal set => SetProperty(ref _isSelected, value); }
    /// <summary>选择后只更新详情。</summary>
    public RelayCommand SelectCommand { get; }
    /// <summary>展开或折叠整枝。</summary>
    public RelayCommand ToggleExpandedCommand { get; }
    /// <summary>上下文菜单复用管理编辑命令。</summary>
    public AsyncRelayCommand EditCommand { get; }
    /// <summary>菜单通过稳定节点身份只重命名。</summary>
    public AsyncRelayCommand RenameCommand { get; }
    /// <summary>上下文菜单复用添加子级命令。</summary>
    public AsyncRelayCommand AddChildCommand { get; }
    /// <summary>上下文菜单复用删除确认命令。</summary>
    public AsyncRelayCommand DeleteCommand { get; }
}
