namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>保留真实传输进度、取消和失败重试，同一项不会同时启动两次。</summary>
public sealed class ObjectStorageTransferViewModel : ObservableObject, IDisposable
{
    private readonly Func<ObjectStorageTransferViewModel, IProgress<ObjectStorageTransferProgress>, CancellationToken, Task> _execute;
    private readonly CancellationToken _lifetime;
    private CancellationTokenSource? _operation;
    private ObjectStorageTransferState _state = ObjectStorageTransferState.Waiting;
    private string _error = "";
    private long _transferred;
    private long _total;
    private bool _running;
    private bool _disposed;
    private Task? _currentRun;

    /// <summary>传输委托保存原来的 Bucket 和对象键，浏览其他目录不会更改目标。</summary>
    public ObjectStorageTransferViewModel(string key, bool upload, long total,
        Func<ObjectStorageTransferViewModel, IProgress<ObjectStorageTransferProgress>, CancellationToken, Task> execute,
        CancellationToken lifetime)
    {
        Key = key;
        IsUpload = upload;
        _total = total;
        _execute = execute;
        _lifetime = lifetime;
        CancelCommand = new RelayCommand(_ => _operation?.Cancel(), _ => IsRunning);
        RetryCommand = new AsyncRelayCommand(_ => RunAsync(), _ => CanRetry);
    }

    /// <summary>任务完成后页面可刷新当前目录，失败任务不会消失。</summary>
    public event EventHandler? Finished;
    /// <summary>真实传输任务对应的对象键。</summary>
    public string Key { get; }
    /// <summary>任务名称使用相对文件名。</summary>
    public string Name => ObjectStoragePresentation.Name(Key);
    /// <summary>上传和下载使用不同方向的图标。</summary>
    public bool IsUpload { get; }
    /// <summary>任务图标。</summary>
    public string Icon => IsFailed ? "triangle-alert" : IsCompleted ? "check" : IsUpload ? "upload" : "download";
    /// <summary>只有传输委托真实完成后才显示完成。</summary>
    public string State => Text.Get(_state.ToString());
    /// <summary>使用稳定状态控制重试和操作，不使用翻译结果判断业务流程。</summary>
    private void SetState(ObjectStorageTransferState value) => SetProperty(ref _state, value, nameof(State));
    /// <summary>业务错误已清理，未知错误不会泄漏本地路径或访问凭据。</summary>
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    /// <summary>仍在等待或传输中的任务可取消。</summary>
    public bool IsRunning { get => _running; private set => SetProperty(ref _running, value); }
    /// <summary>失败和取消的任务支持重试。</summary>
    public bool CanRetry => !_disposed && !IsRunning && _state is ObjectStorageTransferState.Failed or ObjectStorageTransferState.Canceled;
    /// <summary>任务处于失败状态。</summary>
    public bool IsFailed => _state == ObjectStorageTransferState.Failed;
    /// <summary>正常完成的任务。</summary>
    public bool IsCompleted => _state == ObjectStorageTransferState.Completed;
    /// <summary>进度不会超出服务返回的字节范围。</summary>
    public double Percent => _total > 0 ? Math.Clamp((double)_transferred / _total * 100, 0, 100) : 0;
    /// <summary>没有总长度时只显示状态，避免虚构百分比。</summary>
    public string ProgressText => IsFailed ? Error : IsRunning && _total > 0
        ? $"{Percent:0}% · {ObjectStoragePresentation.Size(_transferred)} / {ObjectStoragePresentation.Size(_total)}" : State;
    /// <summary>取消当前任务。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>失败后重新检查同名对象和用户授权。</summary>
    public AsyncRelayCommand RetryCommand { get; }
    /// <summary>页签关闭等待这一任务完成远端中止和本地临时文件清理。</summary>
    public Task Completion => _currentRun ?? Task.CompletedTask;

    /// <summary>由页面启动或用户重试，进度回到创建任务的界面上下文。</summary>
    public Task RunAsync()
    {
        if (_currentRun is { IsCompleted: false }) return _currentRun;
        if (_disposed || _lifetime.IsCancellationRequested) return Task.CompletedTask;
        return _currentRun = RunCoreAsync();
    }

    /// <summary>传输委托完成清理后才结束可等待任务，关闭后不发迟到通知。</summary>
    private async Task RunCoreAsync()
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _operation = operation;
        IsRunning = true;
        SetState(ObjectStorageTransferState.Waiting);
        Error = "";
        _transferred = 0;
        NotifyState();
        var progress = new Progress<ObjectStorageTransferProgress>(value =>
        {
            if (_disposed || !ReferenceEquals(_operation, operation) || !IsRunning) return;
            _total = Math.Max(0, value.TotalBytes);
            _transferred = Math.Clamp(value.TransferredBytes, 0, _total);
            SetState(ObjectStorageTransferState.Transferring);
            NotifyState();
        });
        try
        {
            await _execute(this, progress, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (!_disposed && _state != ObjectStorageTransferState.Skipped) { _transferred = _total; SetState(ObjectStorageTransferState.Completed); }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        { if (!_disposed) SetState(ObjectStorageTransferState.Canceled); }
        catch (Exception exception)
        {
            if (!_disposed) { SetState(ObjectStorageTransferState.Failed); Error = ObjectStoragePresentation.Error(exception, Text.Get("TransferFailedTaskRetainedTryAgain")); }
        }
        finally
        {
            _operation = null;
            if (_disposed) _running = false;
            else { IsRunning = false; NotifyState(); Finished?.Invoke(this, EventArgs.Empty); }
        }
    }

    /// <summary>队列明确开始执行时更新状态。</summary>
    public void MarkTransferring() { SetState(ObjectStorageTransferState.Transferring); NotifyState(); }
    /// <summary>跳过同名对象时保留明确的任务结果。</summary>
    public void MarkSkipped() { SetState(ObjectStorageTransferState.Skipped); NotifyState(); }
    /// <summary>刷新任务状态、图标和操作按钮。</summary>
    private void NotifyState()
    {
        foreach (string property in new[] { nameof(Percent), nameof(ProgressText), nameof(Icon), nameof(IsFailed), nameof(IsCompleted), nameof(CanRetry) })
            OnPropertyChanged(property);
        CancelCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }
    /// <summary>页签关闭取消传输，晚到的进度和完成通知不再更新页面。</summary>
    public void Dispose() { _disposed = true; _operation?.Cancel(); NotifyState(); }
}
