namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>先确认选中的键和目录，再在同一窗体中收集并分批删除对象。</summary>
public sealed class ObjectStorageDeleteDialogViewModel : ObjectStorageDialogViewModel
{
    private readonly Func<IProgress<int>, CancellationToken, Task<ObjectStorageDeletePlan>> _prepare;
    private readonly Func<ObjectStorageDeletePlan, IProgress<int>, CancellationToken, Task> _delete;
    private readonly CancellationToken _workspaceToken;
    private CancellationTokenSource? _operation;
    private bool _confirmed;
    private int? _total;
    private int _count;
    private string _outcome = "";

    /// <summary>固定界面显示的选中范围，构造时不发出请求。</summary>
    public ObjectStorageDeleteDialogViewModel(ObjectStorageBucket bucket, string prefix, IReadOnlyList<ObjectStorageEntry> entries,
        Func<IProgress<int>, CancellationToken, Task<ObjectStorageDeletePlan>> prepare,
        Func<ObjectStorageDeletePlan, IProgress<int>, CancellationToken, Task> delete, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(delete);
        _prepare = prepare;
        _delete = delete;
        _workspaceToken = cancellationToken;
        Location = bucket.Name + (prefix.Length == 0 ? " /" : " / " + prefix.TrimEnd('/'));
        Keys = Array.AsReadOnly(entries.Select(entry => entry.Key).ToArray());
        Warning = Text.Get(entries.Any(entry => entry.IsDirectory)
            ? "ObjectStorageDeleteRecursiveWarning" : "ObjectsWillBeDeletedFromStorage");
        ConfirmCommand = new AsyncRelayCommand(_ => Completion = RunAsync(),
            _ => !IsDisposed && IsConfirming && Keys.Count > 0 && !_workspaceToken.IsCancellationRequested);
        DismissCommand = new RelayCommand(_ => Dismiss(), _ => !IsDisposed && _operation?.IsCancellationRequested != true);
    }

    /// <summary>已确认操作的结束任务，关闭窗体后等待清理再刷新列表。</summary>
    internal Task Completion { get; private set; } = Task.CompletedTask;
    /// <summary>发起过删除才需要从服务端重新读取列表。</summary>
    internal bool DeletionStarted => _total.HasValue;
    /// <summary>工作区关闭时允许系统直接关闭窗体。</summary>
    internal bool IsWorkspaceClosing => _workspaceToken.IsCancellationRequested;

    /// <summary>当前容器和目录位置。</summary>
    public string Location { get; }
    /// <summary>用户选中的完整键，后代清单从不加载到确认视图。</summary>
    public IReadOnlyList<string> Keys { get; }
    /// <summary>包括子目录和不可撤销的影响说明。</summary>
    public string Warning { get; }
    /// <summary>只接受一次显式删除确认。</summary>
    public AsyncRelayCommand ConfirmCommand { get; }
    /// <summary>确认前取消，执行时停止，结束后关闭。</summary>
    public RelayCommand DismissCommand { get; }
    /// <summary>尚未开始收集。</summary>
    public bool IsConfirming => !_confirmed;
    /// <summary>等待收集或删除结束，包括已请求停止的情况。</summary>
    public bool IsRunning => _operation is not null;
    /// <summary>收集过程中显示活动进度，删除后保留已确认的进度。</summary>
    public bool ShowProgress => IsRunning || DeletionStarted;
    /// <summary>收集完之前不知道总量。</summary>
    public bool IsIndeterminate => IsRunning && !DeletionStarted;
    /// <summary>进度条的对象总量和已确认完成量。</summary>
    public int ProgressMaximum => _total ?? 1;
    /// <summary>未知总量时不估算比例。</summary>
    public int ProgressValue => DeletionStarted ? _count : 0;
    /// <summary>确认问题或当前步骤。</summary>
    public string Heading => IsConfirming ? Text.Format("ObjectStorageDeleteConfirmSelection", Keys.Count)
        : Text.Get(DeletionStarted ? "ObjectStorageDeleteStepDelete" : "ObjectStorageDeleteStepCollect");
    /// <summary>收集进度只报当前数量；删除进度显示已经确认的数量与总量。</summary>
    public string ProgressText => _total is { } total ? Text.Format("ObjectStorageDeleteBatchProgress", _count, total)
        : Text.Format("ObjectStorageDeleteCollectProgress", _count);
    /// <summary>正在停止或操作的最终结果。</summary>
    public string Outcome { get => _outcome; private set => SetProperty(ref _outcome, value); }
    /// <summary>同一个安全操作入口在不同阶段的用途。</summary>
    public string DismissLabel => Text.Get(IsRunning ? "ObjectStorageDeleteStop" : IsConfirming ? "Cancel" : "Close");

    private void Dismiss()
    {
        if (_operation is not { } operation)
        {
            Close(false);
            return;
        }
        Outcome = Text.Get("ObjectStorageDeleteStopping");
        operation.Cancel();
        DismissCommand.NotifyCanExecuteChanged();
    }

    private async Task RunAsync()
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_workspaceToken);
        _operation = operation;
        _confirmed = true;
        RefreshStage();
        var token = operation.Token;
        bool IsActive() => !IsDisposed && ReferenceEquals(_operation, operation);
        try
        {
            var collected = new Progress<int>(count => { if (IsActive() && !DeletionStarted) UpdateCount(count); });
            var plan = await _prepare(collected, token);
            token.ThrowIfCancellationRequested();
            if (plan.Keys.Count == 0)
            {
                Outcome = Text.Get("ObjectStorageDeleteEmpty");
                return;
            }

            // 只在完整收集后切换进度；晚到的收集回调不能覆盖删除进度。
            _count = 0;
            _total = plan.Keys.Count;
            RefreshStage();
            var deleted = new Progress<int>(count => { if (IsActive()) UpdateCount(count); });
            await _delete(plan, deleted, token);
            if (!IsDisposed)
            {
                UpdateCount(plan.Keys.Count);
                Outcome = Text.Get("ObjectStorageDeleteCompleted");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!IsDisposed) Outcome = Text.Get(DeletionStarted ? "ObjectStorageDeleteStopped" : "ObjectStorageDeleteCollectStopped");
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                string detail = ObjectStoragePresentation.Error(error, Text.Get("TheOperationFailedCheckTheNetworkAndAccessPermissionsThenTryAgain"));
                Outcome = Text.Format(DeletionStarted ? "ObjectStorageDeleteFailed" : "ObjectStorageDeleteCollectFailed", detail);
            }
        }
        finally
        {
            _operation = null;
            if (!IsDisposed) RefreshStage();
        }
    }

    private void UpdateCount(int count)
    {
        if (count <= _count) return;
        _count = count;
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressText));
    }

    private void RefreshStage()
    {
        foreach (string property in new[] { nameof(IsConfirming), nameof(IsRunning), nameof(ShowProgress), nameof(IsIndeterminate),
            nameof(Heading), nameof(ProgressMaximum), nameof(ProgressValue), nameof(ProgressText), nameof(DismissLabel) })
            OnPropertyChanged(property);
        DismissCommand.NotifyCanExecuteChanged();
    }

    /// <summary>所有关闭路径取消尚未完成的操作，过期回调不再修改视图。</summary>
    public override void Dispose()
    {
        if (IsDisposed) return;
        base.Dispose();
        _operation?.Cancel();
        ConfirmCommand.NotifyCanExecuteChanged();
        DismissCommand.NotifyCanExecuteChanged();
    }
}
