namespace Asgard.Hermod.UI.ViewModels;

/// <summary>存储侧栏的一行目录，缓存已经读取的子前缀并提供独立分页入口。</summary>
public sealed class ObjectStorageNavigationNodeViewModel : ObservableObject
{
    private bool _isExpanded;
    private bool _isCurrent;
    private bool _isLoading;
    private string _error = "";

    /// <summary>由导航树提供命令回调，使节点不接触第三方 SDK。</summary>
    internal ObjectStorageNavigationNodeViewModel(ObjectStorageBucket bucket, string prefix, int depth,
        ObjectStorageNavigationViewModel owner, bool isMore = false)
    {
        Bucket = bucket;
        Prefix = prefix;
        Depth = depth;
        IsMore = isMore;
        OpenCommand = new AsyncRelayCommand(_ => isMore ? owner.LoadMoreAsync(this) : owner.OpenAsync(this),
            _ => !IsLoading);
        ToggleCommand = new AsyncRelayCommand(_ => owner.ToggleAsync(this), _ => !IsLoading && !IsMore);
    }

    /// <summary>节点所属容器。</summary>
    public ObjectStorageBucket Bucket { get; }
    /// <summary>此目录的完整对象前缀，容器根目录为空字符串。</summary>
    public string Prefix { get; }
    /// <summary>容器从零层开始，子前缀增加缩进。</summary>
    public int Depth { get; }
    /// <summary>单独的下一批入口避免遗漏目录多于一页的容器。</summary>
    public bool IsMore { get; }
    /// <summary>目录在列表中的安全显示名称。</summary>
    public string Name => IsMore ? "加载更多目录…" : Depth == 0 ? Bucket.Name : Prefix.TrimEnd('/').Split('/')[^1];
    /// <summary>根目录、已展开目录和分页入口使用不同的官方图标。</summary>
    public string Icon => IsMore ? "ellipsis" : Depth == 0 ? "database" : IsCurrent ? "folder-open" : "folder";
    /// <summary>箭头随展开状态同步。</summary>
    public string ExpandIcon => IsExpanded ? "chevron-down" : "chevron-right";
    /// <summary>缩进与对象目录层级一致。</summary>
    public Thickness Inset => new(22 + Depth * 16, 0, 10, 0);
    /// <summary>是否展开子目录。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        internal set { if (SetProperty(ref _isExpanded, value)) OnPropertyChanged(nameof(ExpandIcon)); }
    }
    /// <summary>页面当前正在浏览的目录。</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        internal set { if (SetProperty(ref _isCurrent, value)) OnPropertyChanged(nameof(Icon)); }
    }
    /// <summary>避免重复展开请求。</summary>
    public bool IsLoading
    {
        get => _isLoading;
        internal set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            OpenCommand.NotifyCanExecuteChanged();
            ToggleCommand.NotifyCanExecuteChanged();
        }
    }
    /// <summary>加载失败可再次点击箭头重试，错误不包含凭据。</summary>
    public string Error { get => _error; internal set => SetProperty(ref _error, value); }
    /// <summary>点击名称进入这个目录。</summary>
    public AsyncRelayCommand OpenCommand { get; }
    /// <summary>展开箭头只读取子目录，不切换当前页面。</summary>
    public AsyncRelayCommand ToggleCommand { get; }
    /// <summary>缓存目录节点，折叠后不丢失已读取的结构。</summary>
    internal List<ObjectStorageNavigationNodeViewModel> Children { get; } = [];
    internal string? NextToken { get; set; }
    internal bool HasLoaded { get; set; }
    internal ObjectStorageNavigationNodeViewModel? Parent { get; init; }
}
