namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>分支独立分页，搜索只过滤已加载对象并保留祖先。</summary>
public sealed class MySqlObjectTree : IDisposable
{
    private readonly IMySqlSession _session;
    private readonly CancellationTokenSource _lifetime;
    private string _search = "";
    private bool _disposed;
    public MySqlObjectTree(IMySqlSession session, CancellationToken lifetime = default)
    {
        _session = session; _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        Root = new(new(MySqlObjectKind.Root, ""), this);
    }
    public MySqlTreeNode Root { get; }
    public bool IsFiltering { get; private set; }
    public async Task LoadAsync(MySqlTreeNode node)
    {
        if (_disposed || !node.CanExpand || node.IsLoading || !node.HasMore || IsFiltering) return;
        int revision = ++node.Revision;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        node.Pending = pending; node.IsLoading = true; node.Status = Text.Get("MySqlLoading"); node.RefreshStatus();
        try
        {
            var page = await _session.ListAsync(node.Object, node.Offset, pending.Token);
            if (_disposed || revision != node.Revision || pending.IsCancellationRequested) return;
            if (!node.Loaded) node.Children.Clear();
            foreach (var item in page.Items)
            {
                var child = new MySqlTreeNode(item, this) { Parent = node };
                if (!node.Children.Any(existing => existing.Identity == child.Identity)) node.Children.Add(child);
            }
            node.Offset += page.Items.Count; node.HasMore = page.HasMore; node.Loaded = true;
            node.Status = node.Children.Count == 0 ? Text.Get("MySqlEmptyBranch") : "";
            if (_search.Length > 0) Filter(_search);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (revision == node.Revision) { node.Status = Text.Error(error, "MySqlCommandFailed"); node.HasMore = true; }
        }
        finally
        {
            if (revision == node.Revision) { node.Pending = null; node.IsLoading = false; node.RefreshStatus(); }
        }
    }
    public async Task RefreshAsync(MySqlTreeNode? node = null)
    {
        node ??= Root;
        Cancel(node); node.Revision++; node.Loaded = false; node.Offset = 0; node.HasMore = true; node.IsLoading = false;
        if (_search.Length > 0) Filter("");
        await LoadAsync(node);
    }
    public void Filter(string search)
    {
        bool wasSearching = _search.Length > 0; _search = search; IsFiltering = true;
        try { foreach (var node in Root.Children) Apply(node, wasSearching); }
        finally { IsFiltering = false; }
        bool Apply(MySqlTreeNode node, bool oldSearch)
        {
            if (!oldSearch && search.Length > 0) node.SavedExpansion = node.IsExpanded;
            bool child = false; foreach (var item in node.Children) child |= Apply(item, oldSearch);
            bool match = search.Length == 0 || !node.IsPlaceholder && (node.Object.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || node.Detail.Contains(search, StringComparison.OrdinalIgnoreCase));
            node.IsVisible = match || child;
            if (search.Length > 0 && child) node.IsExpanded = true;
            if (search.Length == 0 && oldSearch) node.IsExpanded = node.SavedExpansion;
            return node.IsVisible && !node.IsPlaceholder;
        }
    }
    private static void Cancel(MySqlTreeNode node)
    { node.Pending?.Cancel(); foreach (var child in node.Children) Cancel(child); }
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); Cancel(Root); _lifetime.Dispose(); }
}
