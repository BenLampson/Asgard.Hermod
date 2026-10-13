using System.Diagnostics;

namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>限定批次和时间预算，空 SCAN 结果不结束游标；只排序已加载的本层节点。</summary>
public sealed class RedisKeyTree : ObservableObject, IDisposable
{
    private readonly IRedisSession _session;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<string> _discovered = new(StringComparer.Ordinal);
    private int _generation;
    private readonly Dictionary<string, (string Type, string Ttl)> _metadata = new(StringComparer.Ordinal);
    private RedisKeyId? _activeKey;

    /// <summary>一个查询范围拥有一个缓存；切换范围会取消并忽略迟到结果。</summary>
    public RedisKeyTree(IRedisSession session, int database, string pattern, string type)
    {
        _session = session; Database = database; Pattern = pattern.Length == 0 ? "*" : pattern; Type = type;
        Root = CreateNode("", "");
    }
    /// <summary>当前数据库。</summary>
    public int Database { get; }
    /// <summary>用户输入的 Redis glob MATCH，不对用户的表达式做猜测改写。</summary>
    public string Pattern { get; }
    /// <summary>稳定的 Redis TYPE 过滤值。</summary>
    public string Type { get; }
    /// <summary>虚拟根不渲染，界面绑定其子节点。</summary>
    public RedisTreeNode Root { get; }
    /// <summary>实际去重发现数，不宣称总量。</summary>
    public string CountLabel => Text.Format("RedisShownKeys", VisibleKeys(Root));
    /// <summary>部分主节点失败或安全读取反馈。</summary>
    public string Error { get; private set; } = "";
    /// <summary>空反馈不占用共享通知插槽。</summary>
    public bool HasError => Error.Length > 0;

    /// <summary>组前缀是字面量，必须转义 glob 元字符。</summary>
    public static string EscapePrefix(string prefix)
    {
        var result = new StringBuilder();
        foreach (char value in prefix) { if (value is '*' or '?' or '[' or ']' or '\\') result.Append('\\'); result.Append(value); }
        return result.ToString();
    }

    /// <summary>每次最多四批且最多约 250ms，网络单次超时仍由协议层负责。</summary>
    public async Task LoadAsync(RedisTreeNode node)
    {
        if (!node.IsGroup || node.IsLoading || !node.HasMore || _lifetime.IsCancellationRequested) return;
        int generation = _generation;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        node.Pending = pending; node.IsLoading = true; node.Status = Text.Get("RedisLoading");
        var watch = Stopwatch.StartNew();
        try
        {
            if (!node.Loaded) { node.Children.Clear(); node.Loaded = true; }
            int batches = 0;
            // 失败后只重试失败的节点；成功节点保留游标，下一次继续正常推进。
            var candidates = node.Cursors.Any(pair => pair.Value.Failed)
                ? node.Cursors.Where(pair => pair.Value.Failed).ToArray() : node.Cursors.Where(pair => !pair.Value.Complete).ToArray();
            foreach (var pair in candidates)
            {
                if (batches >= 4 || watch.ElapsedMilliseconds >= 250) break;
                batches++;
                try
                {
                    // 前缀和用户 MATCH 不能直接拼接成一个 glob。先按字面前缀扫描，再按用户 glob 二次匹配。
                    var page = await _session.ScanAsync(pair.Key, Database, pair.Value.Cursor,
                        node.Prefix.Length == 0 ? Pattern : EscapePrefix(node.Prefix) + "*", Type, pending.Token);
                    if (pending.IsCancellationRequested || generation != _generation) return;
                    pair.Value.Cursor = page.Cursor; pair.Value.Complete = page.Cursor == "0"; pair.Value.Failed = false;
                    foreach (var key in page.Keys)
                    {
                        if (node.Prefix.Length > 0 && (!key.IsText || !key.DisplayName.StartsWith(node.Prefix, StringComparison.Ordinal))) continue;
                        if (node.Prefix.Length > 0 && !RedisGlob.IsMatch(key.ToBytes(), Encoding.UTF8.GetBytes(Pattern))) continue;
                        _discovered.Add(key.Identity); AddKey(node, key);
                    }
                }
                catch (OperationCanceledException) when (pending.IsCancellationRequested) { return; }
                catch (Exception error) { pair.Value.Failed = true; Error = Text.Error(error, "RedisConnectionFailed"); }
            }
            node.HasMore = node.Cursors.Any(pair => !pair.Value.Complete || pair.Value.Failed);
            bool partial = node.Cursors.Any(pair => pair.Value.Failed);
            if (partial) Error = Text.Get("RedisPartialScan");
            else Error = "";
            node.Status = partial ? Text.Get("RedisPartialScan") : node.HasMore ? Text.Get("RedisMoreGroups") : "";
            OnPropertyChanged(nameof(CountLabel)); OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            if (ReferenceEquals(node.Pending, pending)) node.Pending = null;
            node.IsLoading = false;
            node.LoadMoreCommand?.NotifyCanExecuteChanged();
            if (pending.IsCancellationRequested) node.Status = node.HasMore ? Text.Get("RedisMoreGroups") : "";
        }
    }

    private RedisTreeNode CreateNode(string label, string prefix, RedisKeyId? key = null)
    {
        var node = new RedisTreeNode(label, prefix, key) { Status = key is null ? Text.Get("RedisNotLoaded") : "", HasMore = key is null };
        if (key is null)
        {
            foreach (string server in _session.ScanNodes) node.Cursors.Add(server, new());
            node.Children.Add(new("", "") { Placeholder = true });
            node.LoadMoreCommand = new AsyncRelayCommand(_ => LoadAsync(node), _ => node.HasMore && !node.IsLoading);
            node.ExpansionChanged += OnExpansion;
        }
        return node;
    }
    private static int VisibleKeys(RedisTreeNode node) => node.Children.Sum(child => child.IsGroup ? child.IsExpanded ? VisibleKeys(child) : 0 : 1);
    /// <summary>页签已读取的信息供树展示，不产生额外的值读取。</summary>
    public void Remember(RedisKeyId key, string type, string ttl)
    {
        _metadata[key.Identity] = (type, ttl); Apply(Root);
    }
    /// <summary>活动页签决定键高亮，选择一个前缀不会把前缀当成业务键。</summary>
    public void SetActiveKey(RedisKeyId? key) { _activeKey = key; Apply(Root); }
    private void Apply(RedisTreeNode parent)
    {
        foreach (var child in parent.Children)
        {
            if (child.Key is { } key)
            {
                child.IsActive = _activeKey?.Equals(key) == true;
                if (_metadata.TryGetValue(key.Identity, out var info)) { child.Type = info.Type; child.Ttl = info.Ttl; child.OnMetadataChanged(); }
            }
            else Apply(child);
        }
    }
    /// <summary>折叠只取消未完成读取，保留已经加载的本层缓存。</summary>
    public void CollapseAll()
    {
        void Collapse(RedisTreeNode node) { foreach (var child in node.Children) { child.IsExpanded = false; Collapse(child); } }
        Collapse(Root); OnPropertyChanged(nameof(CountLabel));
    }
    private void AddKey(RedisTreeNode parent, RedisKeyId key)
    {
        string remainder = key.IsText ? key.DisplayName[parent.Prefix.Length..] : key.DisplayName;
        int separator = key.IsText ? remainder.IndexOf(':') : -1;
        RedisTreeNode child = separator >= 0
            ? CreateNode(remainder[..separator] is { Length: > 0 } name ? name : ":", parent.Prefix + remainder[..(separator + 1)])
            : CreateNode(remainder.Length == 0 ? key.DisplayName : remainder, "", key);
        if (!parent.Seen.Add(child.Identity)) return;
        int index = 0;
        while (index < parent.Children.Count && Compare(parent.Children[index], child) <= 0) index++;
        parent.Children.Insert(index, child);
        Apply(parent);
    }
    private static int Compare(RedisTreeNode left, RedisTreeNode right) => left.IsGroup != right.IsGroup ? left.IsGroup ? -1 : 1
        : StringComparer.Ordinal.Compare(left.Label, right.Label);
    private async void OnExpansion(RedisTreeNode node, bool expanded)
    {
        if (!expanded) { CancelBranch(node); OnPropertyChanged(nameof(CountLabel)); return; }
        try { await LoadAsync(node); } catch (Exception error) { Error = Text.Error(error, "RedisConnectionFailed"); OnPropertyChanged(nameof(Error)); }
    }
    private static void CancelBranch(RedisTreeNode node)
    {
        node.Pending?.Cancel(); foreach (var child in node.Children) CancelBranch(child);
    }
    /// <inheritdoc />
    public void Dispose() { _generation++; _lifetime.Cancel(); CancelBranch(Root); _lifetime.Dispose(); }
    /// <summary>离开查询范围时取消读取但保留游标与已加载节点，返回时继续按需读取。</summary>
    public void Suspend() { _generation++; CancelBranch(Root); }
}
