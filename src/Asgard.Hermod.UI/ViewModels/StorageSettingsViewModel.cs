using Asgard.Hermod.UI.Localization;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>后台扫描与确认清理的设置状态，关闭后忽略迟到结果。</summary>
public sealed class StorageSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ITemporaryFileStorage _storage;
    private readonly IStorageSettingsDialogs _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private TemporaryStorageSnapshot? _snapshot;
    private string _message = "";
    private bool _isBusy;
    private bool _disposed;

    /// <summary>通过替换式交互边界测试取消确认，不在状态内部创建窗口。</summary>
    public StorageSettingsViewModel(ITemporaryFileStorage storage, IStorageSettingsDialogs dialogs)
    {
        _storage = storage; _dialogs = dialogs;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !_disposed && !IsBusy);
        CleanupCommand = new AsyncRelayCommand(_ => CleanupAsync(), _ => !_disposed && !IsBusy && Snapshot?.CleanableFileCount > 0);
        OpenFolderCommand = new RelayCommand(_ =>
        {
            try { _storage.EnsureDirectory(); _dialogs.OpenFolder(RootPath); }
            catch (Exception exception) { Message = Text.Error(exception, "StorageOpenFolderFailed"); }
        }, _ => !_disposed && !IsBusy);
    }

    /// <summary>位置只读，不能将配置数据库或任意用户目录作为清理目标。</summary>
    public string RootPath => _storage.RootPath;
    /// <summary>当前真实扫描快照；首次读取失败时不伪造零占用。</summary>
    public TemporaryStorageSnapshot? Snapshot
    {
        get => _snapshot;
        private set
        {
            if (!SetProperty(ref _snapshot, value)) return;
            foreach (string property in new[] { nameof(TotalSize), nameof(FileCountText), nameof(CleanableText), nameof(InUseText), nameof(Usage), nameof(PartialWarning) })
                OnPropertyChanged(property);
            NotifyCommands();
        }
    }
    /// <summary>总占用。</summary>
    public string TotalSize => Snapshot is null ? Text.Get("StorageUnknownSize") : FileSizeText.Format(Snapshot.TotalBytes);
    /// <summary>总文件数，元数据标记不计入正文。</summary>
    public string FileCountText => Snapshot is null ? Text.Get("StorageNotScanned") : Text.Format("StorageFileCount", Snapshot.FileCount);
    /// <summary>可清理容量。</summary>
    public string CleanableText => Snapshot is null ? "" : Text.Format("StorageCleanableSpace", FileSizeText.Format(Snapshot.CleanableBytes));
    /// <summary>占用中的文件会在清理时重新检查并跳过。</summary>
    public string InUseText => Snapshot is null ? "" : Text.Format("StorageInUseSpace", FileSizeText.Format(Snapshot.InUseBytes));
    /// <summary>分类明细只使用扫描结果。</summary>
    public IReadOnlyList<StorageUsageViewModel> Usage => Snapshot?.Usage.Select(usage => new StorageUsageViewModel(usage)).ToArray() ?? [];
    /// <summary>权限失败或链接项目不能被当成不存在。</summary>
    public string PartialWarning => Snapshot?.UnreadableItems > 0 ? Text.Get("StorageStatisticsPartial") : "";
    /// <summary>扫描和清理互斥，确认期间也不允许重复提交。</summary>
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) NotifyCommands(); } }
    /// <summary>首次扫描成功后，窗口加载不重复请求。</summary>
    public bool IsInitialized { get; private set; }
    /// <summary>进度、实际清理结果和安全错误。</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>刷新统计。</summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>确认后只清理不再占用的临时文件。</summary>
    public AsyncRelayCommand CleanupCommand { get; }
    /// <summary>打开专用目录。</summary>
    public RelayCommand OpenFolderCommand { get; }

    /// <summary>由设置宿主预加载一次，失败后保留刷新入口。</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => IsInitialized ? Task.CompletedTask : RefreshAsync(cancellationToken);

    /// <summary>服务负责后台文件 I/O，结果回到创建状态的 Dispatcher 上下文。</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || IsBusy) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        IsBusy = true; Message = Text.Get("StorageScanning");
        try
        {
            var snapshot = await _storage.InspectAsync(operation.Token);
            if (_disposed || operation.IsCancellationRequested) return;
            Snapshot = snapshot; IsInitialized = true; Message = "";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { if (!_disposed) Message = Text.Get("StorageOperationCanceled"); }
        catch (Exception exception) { if (!_disposed) Message = Text.Error(exception, "StorageScanFailed"); }
        finally { if (!_disposed) IsBusy = false; }
    }

    /// <summary>确认显示的是最新扫描，真正删除时服务再次检查文件锁，不依赖过期快照。</summary>
    private async Task CleanupAsync()
    {
        IsBusy = true; Message = Text.Get("StorageScanning");
        CancellationToken token = _lifetime.Token;
        try
        {
            var snapshot = await _storage.InspectAsync(token);
            if (_disposed || token.IsCancellationRequested) return;
            Snapshot = snapshot;
            if (snapshot.CleanableFileCount == 0) { Message = Text.Get("StorageNothingToClean"); return; }
            Message = "";
            if (!await _dialogs.ConfirmCleanupAsync(snapshot.CleanableBytes, snapshot.CleanableFileCount, token)
                || _disposed || token.IsCancellationRequested) return;
            Message = Text.Get("StorageCleaning");
            var result = await _storage.CleanupAsync(cancellationToken: token);
            if (_disposed || token.IsCancellationRequested) return;
            Snapshot = null;
            try
            {
                var refreshed = await _storage.InspectAsync(token);
                if (_disposed || token.IsCancellationRequested) return;
                Snapshot = refreshed;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                if (!_disposed) Message = Text.Format("StorageCleanupRefreshFailed", FileSizeText.Format(result.FreedBytes), result.DeletedFileCount);
                return;
            }
            if (!_disposed) Message = Text.Format("StorageCleanupCompleted", FileSizeText.Format(result.FreedBytes), result.DeletedFileCount, result.SkippedFileCount);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (!_disposed) Message = Text.Error(exception, "StorageCleanupFailed"); }
        finally { if (!_disposed) IsBusy = false; }
    }

    /// <summary>状态变化同步刷新所有命令，避免扫描和清理同时执行。</summary>
    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged(); CleanupCommand.NotifyCanExecuteChanged(); OpenFolderCommand.NotifyCanExecuteChanged();
    }
    /// <summary>关闭窗口取消未完成扫描和确认，迟到结果不再更新页面。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); NotifyCommands();
    }
}
