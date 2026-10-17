namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>分组和真实键使用不同身份；展开分组不读取值。</summary>
public sealed class RedisTreeNode(string label, string prefix, RedisKeyId? key = null) : ObservableObject
{
    /// <summary>显示当前层的名称，完整键保留在工具提示。</summary>
    public string Label { get; } = label;
    /// <summary>分组前缀包含末尾分隔符。</summary>
    public string Prefix { get; } = prefix;
    /// <summary>真实键身份，分组为空。</summary>
    public RedisKeyId? Key { get; } = key;
    /// <summary>用于树状态和节点去重。</summary>
    public string Identity => Key is null ? "group:" + Prefix : "key:" + Key.Identity;
    /// <summary>真实键与同名前缀不是同一个节点。</summary>
    public bool IsGroup => Key is null;
    /// <summary>组显示文件夹，键显示类型无关的数据库图标。</summary>
    public string Icon => IsGroup ? IsExpanded ? "folder-open" : "folder" : Type is "hash" or "list" or "set" or "zset" ? "table-2" : "file";
    /// <summary>只使用已经打开的键读取到的类型，不提前请求值。</summary>
    public string Type { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(Icon)); } } = "";
    /// <summary>已打开键的真实读取元信息。</summary>
    public string Ttl { get; set => SetProperty(ref field, value); } = "";
    /// <summary>未读取值的叶节点不伪造元信息。</summary>
    public bool HasMetadata => Type.Length > 0;
    /// <summary>只有活动键对应的叶节点高亮。</summary>
    public bool IsActive { get; set => SetProperty(ref field, value); }
    /// <summary>悬浮显示完整键或前缀。</summary>
    public string ToolTip => Key?.DisplayName ?? Prefix;
    /// <summary>子节点只在本层展开时读取。</summary>
    public ObservableCollection<RedisTreeNode> Children { get; } = [];
    /// <summary>展开请求由树控制器响应，收起取消尚未完成的读取。</summary>
    public event Action<RedisTreeNode, bool>? ExpansionChanged;
    /// <summary>原生 TreeView 控制键盘展开和收起。</summary>
    public bool IsExpanded
    {
        get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(Icon)); ExpansionChanged?.Invoke(this, value); } }
    }
    /// <summary>状态不显示未经扫描的数量。</summary>
    public string Status { get; set => SetProperty(ref field, value); } = Text.Get("RedisNotLoaded");
    /// <summary>是否仍有游标或失败节点。</summary>
    public bool HasMore { get; set => SetProperty(ref field, value); } = true;
    /// <summary>当前层读取状态。</summary>
    public bool IsLoading { get; set => SetProperty(ref field, value); }
    /// <summary>加载指令只属于当前分组。</summary>
    public AsyncRelayCommand? LoadMoreCommand { get; internal set; }
    internal CancellationTokenSource? Pending { get; set; }
    internal Dictionary<string, RedisNodeCursor> Cursors { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
    internal bool Loaded { get; set; }
    internal bool Placeholder { get; init; }
    internal void OnMetadataChanged() => OnPropertyChanged(nameof(HasMetadata));
}
