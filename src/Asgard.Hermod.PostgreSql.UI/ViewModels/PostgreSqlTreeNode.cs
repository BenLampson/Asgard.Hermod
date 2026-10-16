namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>数据库、类型分组与叶对象各有稳定身份和专用图标。</summary>
public sealed class PostgreSqlTreeNode : ObservableObject
{
    private readonly PostgreSqlObjectTree _tree;
    internal PostgreSqlTreeNode(PostgreSqlObject item, PostgreSqlObjectTree tree, bool placeholder = false)
    {
        Object = item; _tree = tree; IsPlaceholder = placeholder;
        LoadMoreCommand = new AsyncRelayCommand(_ => _tree.LoadAsync(this), _ => HasMore && !IsLoading);
        if (!placeholder && CanExpand) Children.Add(new(new(PostgreSqlObjectKind.Column, ""), tree, true));
    }
    /// <summary>稳定对象身份和原始名称。</summary>
    public PostgreSqlObject Object { get; }
    /// <summary>所属树分支，元数据选择可定位原对象。</summary>
    public PostgreSqlTreeNode? Parent { get; internal set; }
    /// <summary>与翻译无关的去重身份。</summary>
    public string Identity => string.Join('\0', Object.Database, Object.Schema, Object.Kind, Object.ParentName, Object.Name);
    /// <summary>供公共控件展示的已本地化标题。</summary>
    public string Label => IsPlaceholder ? Text.Get("PostgreSqlNotLoaded") : Object.Name.Length > 0 ? Object.Name : Text.Get("PostgreSql" + Object.Kind);
    /// <summary>仅在该分组全部读取完成后展示精确数量，分页未完成时不推算总数。</summary>
    public string DisplayLabel => Loaded && !HasMore && Object.Kind is (PostgreSqlObjectKind.Tables or PostgreSqlObjectKind.Views or PostgreSqlObjectKind.Procedures or PostgreSqlObjectKind.Functions or PostgreSqlObjectKind.Events)
        ? Text.Format("DatabaseObjectGroupCount", Label, Children.Count(node => !node.IsPlaceholder)) : Label;
    /// <summary>具体对象使用紧凑叶行；元数据仍可用键盘方向键展开。</summary>
    public bool IsObjectEntry => Object.Name.Length > 0 && Object.Kind is not (PostgreSqlObjectKind.Root or PostgreSqlObjectKind.Database or PostgreSqlObjectKind.Schema);
    /// <summary>对象图标与容器图标遵循画板的独立大小。</summary>
    public double IconSize => IsObjectEntry ? 13 : 12;
    private double ChildOffset => Object.Kind is PostgreSqlObjectKind.Tables or PostgreSqlObjectKind.Views or PostgreSqlObjectKind.Procedures or PostgreSqlObjectKind.Functions or PostgreSqlObjectKind.Events ? 22 : 12;
    private double VisualIndent => Parent?.Parent is null ? 0 : Parent.VisualIndent + Parent.ChildOffset;
    /// <summary>子级缩进只影响内容，选中背景延伸到整个树视口。</summary>
    public Thickness ChildrenIndent => new(ChildOffset, 0, 0, 0);
    /// <summary>抵消祖先缩进以保持整行的选中背景。</summary>
    public Thickness RowOutset => new(-VisualIndent, 0, 0, 2);
    /// <summary>抵消背景外扩后恢复内容原位。</summary>
    public Thickness RowInset => new(VisualIndent, 0, 0, 0);
    /// <summary>服务器返回的元数据说明。</summary>
    public string Detail => Object.Detail;
    /// <summary>按真实对象类型选择的公共图标。</summary>
    public string Icon => Object.Kind switch
    {
        PostgreSqlObjectKind.Root or PostgreSqlObjectKind.Database => "database",
        PostgreSqlObjectKind.Schema => "layers",
        PostgreSqlObjectKind.Tables or PostgreSqlObjectKind.Table => "table-2",
        PostgreSqlObjectKind.Views or PostgreSqlObjectKind.View => "eye",
        PostgreSqlObjectKind.Procedures or PostgreSqlObjectKind.Procedure => "workflow",
        PostgreSqlObjectKind.Functions or PostgreSqlObjectKind.Function => "square-function",
        PostgreSqlObjectKind.Events or PostgreSqlObjectKind.Event => "clock-3",
        PostgreSqlObjectKind.Columns or PostgreSqlObjectKind.Column => "columns-3",
        PostgreSqlObjectKind.Indexes or PostgreSqlObjectKind.Index => "key-round",
        PostgreSqlObjectKind.ForeignKeys or PostgreSqlObjectKind.ForeignKey => "link",
        PostgreSqlObjectKind.Triggers or PostgreSqlObjectKind.Trigger => "zap",
        _ => "list-filter",
    };
    /// <summary>随浅深主题更新的公共颜色资源键。</summary>
    public string ColorResource => Object.Kind switch
    {
        PostgreSqlObjectKind.Table or PostgreSqlObjectKind.Tables => "SuccessBrush",
        PostgreSqlObjectKind.Function or PostgreSqlObjectKind.Functions => "CodeObjectBrush",
        PostgreSqlObjectKind.Procedure or PostgreSqlObjectKind.Procedures or PostgreSqlObjectKind.Event or PostgreSqlObjectKind.Events
            or PostgreSqlObjectKind.Index or PostgreSqlObjectKind.Indexes or PostgreSqlObjectKind.Trigger or PostgreSqlObjectKind.Triggers => "WarningBrush",
        PostgreSqlObjectKind.Column or PostgreSqlObjectKind.Columns or PostgreSqlObjectKind.Parameter or PostgreSqlObjectKind.Parameters => "TextMutedBrush",
        _ => "AccentBrush",
    };
    /// <summary>此类型是否具有懒加载子分支。</summary>
    public bool CanExpand => Object.Kind is PostgreSqlObjectKind.Root or PostgreSqlObjectKind.Database or PostgreSqlObjectKind.Schema or PostgreSqlObjectKind.Table or PostgreSqlObjectKind.View
        or PostgreSqlObjectKind.Procedure or PostgreSqlObjectKind.Function or PostgreSqlObjectKind.Tables or PostgreSqlObjectKind.Views or PostgreSqlObjectKind.Procedures
        or PostgreSqlObjectKind.Functions or PostgreSqlObjectKind.Events or PostgreSqlObjectKind.Columns or PostgreSqlObjectKind.Indexes or PostgreSqlObjectKind.ForeignKeys or PostgreSqlObjectKind.Triggers or PostgreSqlObjectKind.Parameters;
    /// <summary>只用于提示原生树可展开，不代表真实数据库对象。</summary>
    public bool IsPlaceholder { get; }
    /// <summary>只包含此分支已加载的节点。</summary>
    public ObservableCollection<PostgreSqlTreeNode> Children { get; } = [];
    /// <summary>原生树展开状态。</summary>
    public bool IsExpanded { get; set { if (SetProperty(ref field, value) && value && !_tree.IsFiltering && !Loaded) _ = _tree.LoadAsync(this); } }
    /// <summary>搜索是否保留此节点和祖先。</summary>
    public bool IsVisible { get; set => SetProperty(ref field, value); } = true;
    /// <summary>此分支是否正在读取。</summary>
    public bool IsLoading { get; internal set { if (SetProperty(ref field, value)) LoadMoreCommand.NotifyCanExecuteChanged(); } }
    /// <summary>保留后续分页或失败重试入口。</summary>
    public bool HasMore { get; internal set { if (SetProperty(ref field, value)) LoadMoreCommand.NotifyCanExecuteChanged(); } } = true;
    /// <summary>读取状态或安全失败说明。</summary>
    public string Status { get; internal set => SetProperty(ref field, value); } = "";
    /// <summary>是否需要显示分支状态。</summary>
    public bool HasStatus => Status.Length > 0;
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand LoadMoreCommand { get; }
    internal int Offset { get; set; }
    internal bool Loaded { get; set; }
    internal int Revision { get; set; }
    internal CancellationTokenSource? Pending { get; set; }
    internal bool SavedExpansion { get; set; }
    internal void RefreshStatus() { OnPropertyChanged(nameof(HasStatus)); OnPropertyChanged(nameof(DisplayLabel)); }
}
