namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>无持久 TCP 会话的对象存储以实际请求结果判断连接，失效只弹一次且不自动重放写入。</summary>
public sealed partial class ObjectStorageWorkspaceViewModel
{
    private readonly Dispatcher? _connectionDispatcher = SynchronizationContext.Current is DispatcherSynchronizationContext ? Dispatcher.CurrentDispatcher : null;
    /// <summary>初次访问、认证失败与后续失效共用连接弹窗。</summary>
    public WorkspaceConnectionViewModel Connection { get; } = new();
    /// <inheritdoc />
    public override WorkspaceConnectionViewModel ConnectionState => Connection;
    /// <summary>弹窗开启时暂停页面操作，页面布局不变化。</summary>
    public bool IsConnectionInteractionEnabled => !Connection.IsOpen && !Connection.HasError;
    /// <summary>连接失效已经在弹窗显示，不重复占用列表错误条。</summary>
    public string DisplayActionError => Connection.HasError ? "" : ActionError;
    /// <summary>连接故障仅由弹窗展示；普通目录或权限错误仍可在列表中恢复。</summary>
    public string DisplayReadError => Connection.HasError ? "" : ReadError;
    /// <summary>用户明确重连时重新读取凭据及容器，不重试上传、删除或其他写入。</summary>
    public AsyncRelayCommand ReconnectCommand { get; private set; } = null!;
    private void InitializeConnectionCommands()
    {
        Connection.PropertyChanged += OnConnectionStateChanged;
        ReconnectCommand = new AsyncRelayCommand(_ => RefreshBucketsAsync(), _ => !IsClosed && !Connection.IsConnecting);
    }
    /// <summary>后台传输的连接错误回到页面线程，取消、初次连接及已关闭页面的返回不重复弹窗。</summary>
    private void OnConnectionFailure(Exception error)
    {
        if (_connectionDispatcher?.CheckAccess() == false) { _connectionDispatcher.BeginInvoke(() => OnConnectionFailure(error)); return; }
        if (IsClosed || Connection.IsConnecting) return;
        _hasConnected = false;
        Connection.Disconnected(ObjectStoragePresentation.Error(error, Text.Get("WorkspaceConnectionInterruptedHint")));
        NotifyListState();
    }
    private void OnConnectionStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(Connection.IsOpen) or nameof(Connection.HasError))) return;
        OnPropertyChanged(nameof(IsConnectionInteractionEnabled)); OnPropertyChanged(nameof(DisplayActionError)); OnPropertyChanged(nameof(DisplayReadError));
        NotifyDetails(); NotifyListState();
        ReconnectCommand?.NotifyCanExecuteChanged();
    }
    /// <summary>容器读取失败保留错误，重试时明确重新请求。</summary>
    private async Task ReadBucketsAsync(CancellationToken cancellationToken)
    {
        int version = ++_bucketVersion;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _bucketOperation?.Cancel(); _bucketOperation = operation;
        Connection.Begin(operation.Cancel);
        IsLoading = true; ReadError = "";
        try
        {
            var buckets = await _service.ListBucketsAsync(_instance.Id, operation.Token);
            if (IsClosed || operation.IsCancellationRequested || version != _bucketVersion) return;
            Buckets.Clear();
            foreach (var bucket in buckets) Buckets.Add(bucket);
            HasConnectedOnce = true; _hasConnected = true; IsInitialized = true;
            BucketsChanged?.Invoke(this, EventArgs.Empty);
            if (Buckets.Count > 0) await NavigateAsync(Buckets[0], "", operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception exception) { if (!IsClosed && version == _bucketVersion) ReadError = ObjectStoragePresentation.Error(exception, Text.Get("CheckTheNetworkAndRAMPermissionsToListObjectsInThisBucket")); }
        finally
        {
            if (ReferenceEquals(_bucketOperation, operation)) _bucketOperation = null;
            if (!IsClosed && version == _bucketVersion && _listOperation is null) IsLoading = false;
            if (version == _bucketVersion) Connection.Finish(IsClosed || operation.IsCancellationRequested ? "" : ReadError);
        }
    }

}
