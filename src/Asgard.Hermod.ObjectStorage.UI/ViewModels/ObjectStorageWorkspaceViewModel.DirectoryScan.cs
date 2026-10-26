using System.Globalization;

namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

public sealed partial class ObjectStorageWorkspaceViewModel
{
    private CancellationTokenSource? _scanOperation;
    private TaskCompletionSource<bool>? _scanDecision;
    private bool _isScanning;
    private bool _isScanPaused;
    private bool _isScanComplete;
    private bool _isScanSuccessful;
    private long _scannedFiles;
    private string _scanStatus = "";
    private int _timeSortDirection;

    /// <summary>固定索引代表最新或最早在前；切换完整结果只在内存重排，不重复扫描。</summary>
    public int TimeSortDirection
    {
        get => _timeSortDirection;
        set
        {
            if (value is < 0 or > 1 || !SetProperty(ref _timeSortDirection, value)) return;
            if (IsScanComplete) ApplyTimeSort();
        }
    }

    /// <summary>文件夹始终在前，未知日期始终在末尾；保留行对象、选择与勾选状态。</summary>
    private void ApplyTimeSort()
    {
        var selected = SelectedEntry;
        var grouped = Entries.OrderByDescending(entry => entry.IsDirectory).ThenByDescending(entry => entry.Entry.LastModified.HasValue);
        var ordered = (TimeSortDirection == 0 ? grouped.ThenByDescending(entry => entry.Entry.LastModified)
            : grouped.ThenBy(entry => entry.Entry.LastModified)).ThenBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        for (int index = 0; index < ordered.Length; index++)
        {
            int current = Entries.IndexOf(ordered[index]);
            if (current != index) Entries.Move(current, index);
        }
        SelectedEntry = selected;
    }

    /// <summary>临时递归获取日期，完整扫描后才发布排序。</summary>
    public AsyncRelayCommand ScanDirectoryCommand { get; private set; } = null!;
    /// <summary>取消扫描或暂停等待，保留扫描前的列表。</summary>
    public RelayCommand CancelDirectoryScanCommand { get; private set; } = null!;
    /// <summary>确认大量文件后继续本次扫描。</summary>
    public RelayCommand ContinueDirectoryScanCommand { get; private set; } = null!;
    /// <summary>当前扫描或等待用户决定。</summary>
    public bool IsScanning { get => _isScanning; private set => SetProperty(ref _isScanning, value); }
    /// <summary>扫描达到阈值，暂停读取后续页。</summary>
    public bool IsScanPaused { get => _isScanPaused; private set => SetProperty(ref _isScanPaused, value); }
    /// <summary>当前列表的目录日期来自完整扫描。</summary>
    public bool IsScanComplete { get => _isScanComplete; private set => SetProperty(ref _isScanComplete, value); }
    /// <summary>仅完成提示呈现成功色；上次完整列表在重新扫描或取消时仍可保留。</summary>
    public bool IsScanSuccessful { get => _isScanSuccessful; private set => SetProperty(ref _isScanSuccessful, value); }
    /// <summary>本次已读的实际文件数，目录占位不计入文件。</summary>
    public long ScannedFiles { get => _scannedFiles; private set => SetProperty(ref _scannedFiles, value); }
    /// <summary>扫描状态按完整句子模板翻译。</summary>
    public string ScanStatus { get => _scanStatus; private set { if (SetProperty(ref _scanStatus, value)) OnPropertyChanged(nameof(HasScanStatus)); } }
    /// <summary>初始状态只展示入口旁的范围说明。</summary>
    public bool HasScanStatus => ScanStatus.Length > 0;
    /// <summary>完整扫描后明确目录列日期的汇总含义。</summary>
    public string ModifiedHeading => Text.Get(IsScanComplete ? "DirectoryLatestModified" : "Modified");

    private void InitializeDirectoryScanCommands()
    {
        ScanDirectoryCommand = new AsyncRelayCommand(_ => ScanDirectoryAsync(),
            _ => CanOperate && !IsLoading && SearchText.Length == 0);
        CancelDirectoryScanCommand = new RelayCommand(_ =>
        {
            _scanOperation?.Cancel();
            ScanStatus = Text.Get("DirectoryScanCanceled");
        }, _ => IsScanning);
        ContinueDirectoryScanCommand = new RelayCommand(_ => _scanDecision?.TrySetResult(true), _ => IsScanPaused);
    }

    /// <summary>在页面上下文发布进度，切换目录、关闭或取消后的迟到消息全部丢弃。</summary>
    public async Task ScanDirectoryAsync(CancellationToken cancellationToken = default)
    {
        if (!CanOperate || IsLoading || SearchText.Length > 0) return;
        var bucket = SelectedBucket!;
        string prefix = Prefix;
        int version = _navigationVersion;
        var context = SynchronizationContext.Current;
        var language = CultureInfo.CurrentUICulture;
        var formatting = CultureInfo.CurrentCulture;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _scanOperation = operation;
        IsScanning = true; IsScanPaused = false; IsScanSuccessful = false; ScannedFiles = 0;
        ScanStatus = Text.Format("DirectoryScanProgress", 0);
        RefreshScanCommands(); RefreshCommands();

        bool IsCurrent() => !IsClosed && !operation.IsCancellationRequested && version == _navigationVersion
            && ReferenceEquals(_scanOperation, operation);
        void Publish(Action action)
        {
            void Apply()
            {
                if (!IsCurrent()) return;
                // Dispatcher 的 Post 不传递调用者文化，保留启动本次操作时的显示语言与区域格式。
                var previousLanguage = CultureInfo.CurrentUICulture;
                var previousFormatting = CultureInfo.CurrentCulture;
                try { CultureInfo.CurrentUICulture = language; CultureInfo.CurrentCulture = formatting; action(); }
                finally { CultureInfo.CurrentUICulture = previousLanguage; CultureInfo.CurrentCulture = previousFormatting; }
            }
            if (context is null || ReferenceEquals(context, SynchronizationContext.Current)) Apply();
            else context.Post(_ => Apply(), null);
        }
        var progress = new Progress<ObjectStorageDirectoryScanProgress>(state => Publish(() =>
        {
            ScannedFiles = state.FilesScanned;
            if (!IsScanPaused) ScanStatus = Text.Format("DirectoryScanProgress", ScannedFiles);
        }));
        try
        {
            var result = await _service.ScanDirectoryAsync(_instance.Id, bucket, prefix, async (state, token) =>
            {
                var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Publish(() =>
                {
                    _scanDecision = decision; ScannedFiles = state.FilesScanned;
                    IsScanPaused = true; ScanStatus = Text.Format("DirectoryScanLarge", ScannedFiles);
                    RefreshScanCommands();
                });
                bool confirmed = await decision.Task.WaitAsync(token).ConfigureAwait(false);
                Publish(() => { IsScanPaused = false; ScanStatus = Text.Format("DirectoryScanProgress", ScannedFiles); RefreshScanCommands(); });
                return confirmed;
            }, progress, operation.Token);
            if (!IsCurrent())
            {
                if (!IsClosed && version == _navigationVersion && ReferenceEquals(_scanOperation, operation))
                    ScanStatus = Text.Get("DirectoryScanCanceled");
                return;
            }
            if (result is null) { ScanStatus = Text.Get("DirectoryScanCanceled"); return; }
            var checkedKeys = Entries.Where(entry => entry.IsChecked).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
            string? selectedKey = SelectedEntry?.Key;
            Entries.Clear();
            foreach (var entry in result.Entries) Entries.Add(new(entry, NotifyChecked) { IsChecked = checkedKeys.Contains(entry.Key) });
            ApplyTimeSort();
            _nextToken = null;
            SelectedEntry = Entries.FirstOrDefault(entry => entry.Key == selectedKey);
            ScannedFiles = result.FilesScanned;
            IsScanComplete = true; IsScanSuccessful = true; OnPropertyChanged(nameof(ModifiedHeading));
            ScanStatus = Text.Get("DirectoryScanCompleted");
            _hasConnected = true; ReadError = "";
            NotifyChecked(); NotifyListState(); NavigationChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (!IsClosed && version == _navigationVersion && ReferenceEquals(_scanOperation, operation))
                ScanStatus = Text.Get("DirectoryScanCanceled");
        }
        catch (Exception exception)
        {
            if (IsCurrent()) ScanStatus = Text.Error(exception, "DirectoryScanFailed");
        }
        finally
        {
            if (ReferenceEquals(_scanOperation, operation))
            {
                _scanOperation = null; _scanDecision = null;
                IsScanning = false; IsScanPaused = false;
                RefreshScanCommands(); RefreshCommands();
            }
        }
    }

    /// <summary>刷新、搜索和导航均失效临时日期，不复用扫描结果。</summary>
    private void ResetDirectoryScan()
    {
        _scanOperation?.Cancel(); _scanOperation = null;
        _scanDecision = null; IsScanning = false; IsScanPaused = false; IsScanComplete = false; IsScanSuccessful = false;
        ScannedFiles = 0; ScanStatus = ""; OnPropertyChanged(nameof(ModifiedHeading));
        RefreshScanCommands();
    }

    private void RefreshScanCommands()
    {
        ScanDirectoryCommand.NotifyCanExecuteChanged();
        CancelDirectoryScanCommand.NotifyCanExecuteChanged();
        ContinueDirectoryScanCommand.NotifyCanExecuteChanged();
    }
}
