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
