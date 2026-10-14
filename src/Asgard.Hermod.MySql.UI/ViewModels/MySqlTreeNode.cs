namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>数据库、类型分组与叶对象各有稳定身份和专用图标。</summary>
public sealed class MySqlTreeNode : ObservableObject
{
    private readonly MySqlObjectTree _tree;
    internal MySqlTreeNode(MySqlObject item, MySqlObjectTree tree, bool placeholder = false)
    {
        Object = item; _tree = tree; IsPlaceholder = placeholder;
        LoadMoreCommand = new AsyncRelayCommand(_ => _tree.LoadAsync(this), _ => HasMore && !IsLoading);
        if (!placeholder && CanExpand) Children.Add(new(new(MySqlObjectKind.Column, ""), tree, true));
    }
    public MySqlObject Object { get; }
    public MySqlTreeNode? Parent { get; internal set; }
    public string Identity => string.Join('\0', Object.Database, Object.Kind, Object.ParentName, Object.Name);
    public string Label => IsPlaceholder ? Text.Get("MySqlNotLoaded") : Object.Name.Length > 0 ? Object.Name : Text.Get("MySql" + Object.Kind);
    public string Detail => Object.Detail;
    public string Icon => Object.Kind switch
    {
        MySqlObjectKind.Root or MySqlObjectKind.Database => "database",
        MySqlObjectKind.Tables or MySqlObjectKind.Table => "table-2",
        MySqlObjectKind.Views or MySqlObjectKind.View => "eye",
        MySqlObjectKind.Procedures or MySqlObjectKind.Procedure => "workflow",
        MySqlObjectKind.Functions or MySqlObjectKind.Function => "square-function",
        MySqlObjectKind.Events or MySqlObjectKind.Event => "clock-3",
        MySqlObjectKind.Columns or MySqlObjectKind.Column => "columns-3",
        MySqlObjectKind.Indexes or MySqlObjectKind.Index => "key-round",
        MySqlObjectKind.ForeignKeys or MySqlObjectKind.ForeignKey => "link",
        MySqlObjectKind.Triggers or MySqlObjectKind.Trigger => "zap",
        _ => "list-filter",
    };
    public string ColorResource => Object.Kind switch
    {
        MySqlObjectKind.Table or MySqlObjectKind.Tables => "SuccessBrush",
        MySqlObjectKind.Function or MySqlObjectKind.Functions => "CodeObjectBrush",
        MySqlObjectKind.Procedure or MySqlObjectKind.Procedures or MySqlObjectKind.Event or MySqlObjectKind.Events
            or MySqlObjectKind.Index or MySqlObjectKind.Indexes or MySqlObjectKind.Trigger or MySqlObjectKind.Triggers => "WarningBrush",
        MySqlObjectKind.Column or MySqlObjectKind.Columns or MySqlObjectKind.Parameter or MySqlObjectKind.Parameters => "TextMutedBrush",
        _ => "AccentBrush",
    };
    public bool CanExpand => Object.Kind is MySqlObjectKind.Root or MySqlObjectKind.Database or MySqlObjectKind.Table or MySqlObjectKind.View
        or MySqlObjectKind.Procedure or MySqlObjectKind.Function or MySqlObjectKind.Tables or MySqlObjectKind.Views or MySqlObjectKind.Procedures
        or MySqlObjectKind.Functions or MySqlObjectKind.Events or MySqlObjectKind.Columns or MySqlObjectKind.Indexes or MySqlObjectKind.ForeignKeys or MySqlObjectKind.Triggers or MySqlObjectKind.Parameters;
    public bool IsPlaceholder { get; }
    public ObservableCollection<MySqlTreeNode> Children { get; } = [];
    public bool IsExpanded { get; set { if (SetProperty(ref field, value) && value && !_tree.IsFiltering && !Loaded) _ = _tree.LoadAsync(this); } }
    public bool IsVisible { get; set => SetProperty(ref field, value); } = true;
    public bool IsLoading { get; internal set { if (SetProperty(ref field, value)) LoadMoreCommand.NotifyCanExecuteChanged(); } }
    public bool HasMore { get; internal set { if (SetProperty(ref field, value)) LoadMoreCommand.NotifyCanExecuteChanged(); } } = true;
    public string Status { get; internal set => SetProperty(ref field, value); } = "";
    public bool HasStatus => Status.Length > 0;
    public AsyncRelayCommand LoadMoreCommand { get; }
    internal int Offset { get; set; }
    internal bool Loaded { get; set; }
    internal int Revision { get; set; }
    internal CancellationTokenSource? Pending { get; set; }
    internal bool SavedExpansion { get; set; }
    internal void RefreshStatus() => OnPropertyChanged(nameof(HasStatus));
}
