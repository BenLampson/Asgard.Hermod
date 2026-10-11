namespace Asgard.Hermod.UI.ViewModels;

/// <summary>为存储实例呈现可按需展开的容器及前缀，与页签里的路径导航同步。</summary>
public sealed class ObjectStorageNavigationViewModel : ObservableObject, IDisposable
{
    private readonly ObjectStorageWorkspaceViewModel _page;
    private readonly IObjectStorageService _storage;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<ObjectStorageNavigationNodeViewModel> _roots = [];
    private readonly Dictionary<ObjectStorageNavigationNodeViewModel, Task> _loading = [];
    private bool _disposed;
    private string _error = "";
    private long _revision;

    /// <summary>树只订阅已打开的实例页，不为其他实例自动连接服务。</summary>
    public ObjectStorageNavigationViewModel(ObjectStorageWorkspaceViewModel page, IObjectStorageService storage)
    {
        _page = page;
        _storage = storage;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        _page.BucketsChanged += OnBucketsChanged;
        _page.NavigationChanged += OnNavigationChanged;
        _page.PropertyChanged += OnPagePropertyChanged;
        RebuildRoots();
    }

    /// <summary>按展开状态排列的目录行，分页入口也属于同一组件。</summary>
    public ObservableCollection<ObjectStorageNavigationNodeViewModel> Rows { get; } = [];
    /// <summary>只统计已经读取到的容器，不统计整个容器的容量。</summary>
    public int BucketCount => _roots.Count;
    /// <summary>实例稳定标识，用于跟踪侧栏条目。</summary>
    public string InstanceId => _page.InstanceId!;
    /// <summary>只显示固定或适配器白名单内的错误文案。</summary>
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    /// <summary>刷新当前实例的容器及目录缓存。</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>点击目录名称，由工作页负责取消旧列表并加载新路径。</summary>
    internal Task OpenAsync(ObjectStorageNavigationNodeViewModel node) => _disposed ? Task.CompletedTask :
        _page.NavigateAsync(node.Bucket, node.Prefix, _lifetime.Token);

    /// <summary>展开目录只读取这一层，折叠会保留已读缓存。</summary>
    internal async Task ToggleAsync(ObjectStorageNavigationNodeViewModel node)
    {
        if (_disposed) return;
        node.IsExpanded = !node.IsExpanded;
        RebuildRows();
        if (node.IsExpanded && !node.HasLoaded)
            await LoadChildrenAsync(node);
    }

    /// <summary>下一批目录不切换工作页，继续使用同一查询游标。</summary>
    internal Task LoadMoreAsync(ObjectStorageNavigationNodeViewModel row) =>
        row.Parent is { } parent ? LoadChildrenAsync(parent) : Task.CompletedTask;

    /// <summary>同一节点的并发展开共用一次读取，关闭后不再更新集合。</summary>
    private Task LoadChildrenAsync(ObjectStorageNavigationNodeViewModel node)
    {
        if (_disposed) return Task.CompletedTask;
        if (_loading.TryGetValue(node, out var pending)) return pending;
        var operation = LoadChildrenCoreAsync(node);
        _loading[node] = operation;
        return AwaitAndRemoveAsync(node, operation);
    }

    /// <summary>按页列出一级子前缀；分页行保留文件占满一页时的后续读取机会。</summary>
    private async Task LoadChildrenCoreAsync(ObjectStorageNavigationNodeViewModel node)
    {
        node.IsLoading = true;
        node.Error = "";
        try
        {
            var page = await _storage.ListObjectsAsync(InstanceId, node.Bucket, node.Prefix,
                continuationToken: node.NextToken, cancellationToken: _lifetime.Token);
            if (_disposed || !_roots.Contains(RootOf(node))) return;
            node.Children.RemoveAll(child => child.IsMore);
            foreach (var entry in page.Entries.Where(entry => entry.IsDirectory && entry.Key != node.Prefix))
            {
                if (node.Children.Any(child => child.Prefix == entry.Key)) continue;
                node.Children.Add(new ObjectStorageNavigationNodeViewModel(node.Bucket, entry.Key, node.Depth + 1, this)
                { Parent = node });
            }
            node.NextToken = page.NextToken;
            node.HasLoaded = true;
            if (!string.IsNullOrEmpty(page.NextToken))
                node.Children.Add(new ObjectStorageNavigationNodeViewModel(node.Bucket, node.Prefix, node.Depth + 1,
                    this, isMore: true)
                { Parent = node });
            RebuildRows();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                node.Error = exception is ObjectStorageException safe ? safe.Message : "无法读取目录，点击箭头重试。";
                Error = node.Error;
                node.IsExpanded = false;
                RebuildRows();
            }
        }
        finally { node.IsLoading = false; }
    }

    /// <summary>完成后移除并发任务索引，失败或取消允许下一次展开重试。</summary>
    private async Task AwaitAndRemoveAsync(ObjectStorageNavigationNodeViewModel node, Task operation)
    {
        try { await operation; }
        finally { _loading.Remove(node); }
    }

    /// <summary>重新载入容器后清除目录缓存，当前浏览位置由工作页保留。</summary>
    private async Task RefreshAsync()
    {
        Error = "";
        _roots.Clear();
        await _page.RefreshBucketsAsync(_lifetime.Token);
        RebuildRoots();
        await SynchronizePathAsync();
    }

    /// <summary>容器刷新保留没有改变的节点；编辑连接后即使同名也重新载入。</summary>
    private void RebuildRoots()
    {
        var oldRoots = _roots.ToArray();
        _roots.Clear();
        foreach (var bucket in _page.Buckets)
            _roots.Add(oldRoots.FirstOrDefault(node => node.Bucket == bucket)
                ?? new ObjectStorageNavigationNodeViewModel(bucket, "", 0, this));
        OnPropertyChanged(nameof(BucketCount));
        RebuildRows();
    }

    /// <summary>路径变化后按需展开祖先，不搜索整个容器。</summary>
    private async Task SynchronizePathAsync()
    {
        long revision = ++_revision;
        var current = _roots.FirstOrDefault(node => node.Bucket == _page.SelectedBucket);
        if (current is null || _disposed) { RebuildRows(); return; }
        string path = _page.Prefix;
        while (current is not null && !_disposed && revision == _revision)
        {
            current.IsExpanded = true;
            if (!current.HasLoaded) await LoadChildrenAsync(current);
            if (revision != _revision) break;
            if (current.Prefix == path)
            {
                MergeCurrentDirectory(current);
                break;
            }
            var next = current.Children.Where(node => !node.IsMore && path.StartsWith(node.Prefix,
                StringComparison.Ordinal)).OrderByDescending(node => node.Prefix.Length).FirstOrDefault();
            // 用户通过面包屑进入的合法前缀可能在后续页，直接补入当前路径而不遍历全部文件。
            if (next is null)
            {
                string remainder = path[current.Prefix.Length..];
                int slash = remainder.IndexOf('/');
                if (slash < 0) break;
                next = new ObjectStorageNavigationNodeViewModel(current.Bucket,
                    current.Prefix + remainder[..(slash + 1)], current.Depth + 1, this)
                { Parent = current };
                current.Children.Insert(0, next);
            }
            current = next;
        }
        if (!_disposed) RebuildRows();
    }

    /// <summary>同步已完成的完整目录列表，新建或删除后不用重新打开实例。</summary>
    private void MergeCurrentDirectory(ObjectStorageNavigationNodeViewModel node)
    {
        if (_page.IsLoading || _page.ReadError.Length > 0 || _page.SearchText.Length > 0
            || node.Bucket != _page.SelectedBucket || node.Prefix != _page.Prefix) return;
        var keys = _page.Entries.Where(entry => entry.IsDirectory && entry.Key != node.Prefix
            && entry.Key.StartsWith(node.Prefix, StringComparison.Ordinal)
            && !entry.Key[node.Prefix.Length..].TrimEnd('/').Contains('/'))
            .Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        if (!_page.HasMore)
        {
            node.Children.RemoveAll(child => child.IsMore || !keys.Contains(child.Prefix));
            node.NextToken = null;
        }
        foreach (string key in keys.Order(StringComparer.Ordinal))
            if (!node.Children.Any(child => !child.IsMore && child.Prefix == key))
                node.Children.Insert(node.Children.FindLastIndex(child => !child.IsMore) + 1,
                    new ObjectStorageNavigationNodeViewModel(node.Bucket, key, node.Depth + 1, this) { Parent = node });
        node.HasLoaded = true;
    }

    /// <summary>展开节点组成线性视图，长名字省略而不挤占外壳。</summary>
    private void RebuildRows()
    {
        Rows.Clear();
        foreach (var root in _roots) AddVisible(root);
    }

    /// <summary>当前页路径在树中始终有唯一高亮。</summary>
    private void AddVisible(ObjectStorageNavigationNodeViewModel node)
    {
        node.IsCurrent = !node.IsMore && node.Bucket == _page.SelectedBucket && node.Prefix == _page.Prefix;
        Rows.Add(node);
        if (node.IsExpanded)
            foreach (var child in node.Children) AddVisible(child);
    }

    private static ObjectStorageNavigationNodeViewModel RootOf(ObjectStorageNavigationNodeViewModel node) =>
        node.Parent is { } parent ? RootOf(parent) : node;

    /// <summary>容器事件与路径事件统一复用同一同步过程。</summary>
    private async void OnBucketsChanged(object? sender, EventArgs args)
    {
        _roots.Clear();
        RebuildRoots();
        await SynchronizePathAsync();
    }

    private async void OnNavigationChanged(object? sender, EventArgs args) => await SynchronizePathAsync();

    /// <summary>页签关闭先解绑，避免迟到请求更改已回收的导航。</summary>
    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkspacePageViewModel.IsClosed) && _page.IsClosed) Dispose();
    }

    /// <summary>离开应用时解除页签订阅并终止目录读取。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _page.BucketsChanged -= OnBucketsChanged;
        _page.NavigationChanged -= OnNavigationChanged;
        _page.PropertyChanged -= OnPagePropertyChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        Rows.Clear();
        _roots.Clear();
    }
}
