namespace Asgard.Hermod.UI.ViewModels;

/// <summary>工作页公共状态；关闭时先通知视图解绑，再释放会话。</summary>
public abstract class WorkspacePageViewModel : ObservableObject, IAsyncDisposable
{
    private bool _isSelected;
    private string _header = "";
    private bool _isClosed;
    private Task? _closeTask;

    /// <summary>初始化页签标识与显示信息。</summary>
    protected WorkspacePageViewModel(string id, string header, string icon, string? instanceId = null,
        bool canClose = true, bool hasAlert = false)
    {
        Id = id;
        Header = header;
        Icon = icon;
        InstanceId = instanceId;
        CanClose = canClose;
        HasAlert = hasAlert;
    }

    /// <summary>页签唯一标识。</summary>
    public string Id { get; }
    /// <summary>页签标题；首页只展示图标。</summary>
    public string Header { get => _header; protected set => SetProperty(ref _header, value); }
    /// <summary>只刷新实例显示名称，保留页面身份、连接、内部页签和草稿。</summary>
    public void UpdateInstanceMetadata(InstanceSummary instance)
    {
        if (InstanceId == instance.Id) Header = instance.Name;
    }
    /// <summary>页签图标。</summary>
    public string Icon { get; }
    /// <summary>关联实例标识。</summary>
    public string? InstanceId { get; }
    /// <summary>是否允许用户关闭页签。</summary>
    public bool CanClose { get; }
    /// <summary>展示异常状态圆点。</summary>
    public bool HasAlert { get; }
    /// <summary>是否为活动页签。</summary>
    public bool IsSelected { get => _isSelected; internal set => SetProperty(ref _isSelected, value); }
    /// <summary>已进入关闭流程，视图可以释放缓存。</summary>
    public bool IsClosed => _isClosed;

    /// <summary>页签激活时初始化模块资源，默认页面无需额外操作。</summary>
    public virtual Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    /// <summary>用户关闭页签前的业务确认；应用资源回收直接调用 CloseAsync。</summary>
    public virtual Task<bool> RequestCloseAsync() => Task.FromResult(true);
    /// <summary>全应用退出先收集决定；有关闭副作用的模块必须覆写以延后提交。</summary>
    public virtual async Task<WorkspaceClosePlan?> PrepareCloseAsync() => await RequestCloseAsync()
        ? new WorkspaceClosePlan(() => Task.CompletedTask) : null;
    /// <summary>类型页面可请求新的独立会话，由外壳管理外层页签。</summary>
    public event EventHandler? NewSessionRequested;
    /// <summary>不创建嵌套页签，也不让功能页面访问外壳集合。</summary>
    protected void RequestNewSession() => NewSessionRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>幂等关闭；该入口由 UI 线程调用以保证解绑顺序。</summary>
    public Task CloseAsync()
    {
        if (_closeTask is not null)
            return _closeTask;
        _isClosed = true;
        OnPropertyChanged(nameof(IsClosed));
        _closeTask = ReleaseAsync().AsTask();
        return _closeTask;
    }

    /// <summary>由具体页面释放其应用层句柄。</summary>
    protected virtual ValueTask ReleaseAsync() => ValueTask.CompletedTask;

    /// <summary>应用关闭时使用与页签关闭相同的流程。</summary>
    public ValueTask DisposeAsync() => new(CloseAsync());
}
