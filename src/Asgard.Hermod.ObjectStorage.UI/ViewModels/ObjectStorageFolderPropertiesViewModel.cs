namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>目录属性先取得用户开始操作，再扫描；关闭、取消和迟到进度不能发布不完整结果。</summary>
public sealed class ObjectStorageFolderPropertiesViewModel : ObjectStorageDialogViewModel
{
    private readonly Func<IProgress<ObjectStorageFolderStatistics>, CancellationToken, Task<ObjectStorageFolderStatistics>> _scan;
    private readonly CancellationTokenSource _lifetime;
    private CancellationTokenSource? _operation;
    private ObjectStorageFolderStatistics _statistics = new(0, 0, 0);
    private bool _isRunning;
    private bool _isComplete;
    private string _status = Text.Get("FolderStatisticsWarning");

    /// <summary>捕获明确容器与前缀，构造或打开窗口不会读取远端对象。</summary>
    public ObjectStorageFolderPropertiesViewModel(ObjectStorageBucket bucket, string prefix,
        Func<IProgress<ObjectStorageFolderStatistics>, CancellationToken, Task<ObjectStorageFolderStatistics>> scan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        _scan = scan;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Location = bucket.Name + "/" + prefix;
        Name = prefix.Length == 0 ? bucket.Name : prefix.TrimEnd('/').Split('/').Last();
        StartCommand = new AsyncRelayCommand(_ => StartAsync(), _ => !IsDisposed && !IsRunning && !IsComplete && !_lifetime.IsCancellationRequested);
        StopCommand = new RelayCommand(_ => _operation?.Cancel(), _ => IsRunning && !IsDisposed);
    }

    /// <summary>所选目录或容器名称，属于用户数据。</summary>
    public string Name { get; }
    /// <summary>完整远端位置，不受随后页面导航影响。</summary>
    public string Location { get; }
    /// <summary>显式开始或失败后重试。</summary>
    public AsyncRelayCommand StartCommand { get; }
    /// <summary>取消枚举，保留窗口且不将部分计数显示成结果。</summary>
    public RelayCommand StopCommand { get; }
    /// <summary>总量未知时使用不定进度条，避免伪造百分比。</summary>
    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }
    /// <summary>只在完整枚举后展示最终属性。</summary>
    public bool IsComplete { get => _isComplete; private set => SetProperty(ref _isComplete, value); }
    /// <summary>警告、进度、取消或安全错误。</summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>可读大小和精确字节数同时展示。</summary>
    public string Size => Text.Format("FolderStatisticsSize", ObjectStoragePresentation.Size(_statistics.Bytes), _statistics.Bytes);
    /// <summary>文件与后代目录数，不包含选中目录本身。</summary>
    public string Contains => Text.Format("FolderStatisticsContains", _statistics.Files, _statistics.Folders);
    /// <summary>文件中最近的更新时间；空目录不捏造创建或更新时间。</summary>
    public string Modified => ObjectStoragePresentation.Modified(_statistics.LastModified);

    /// <summary>完整结果与进度分别发布，忽略取消、完成和窗口关闭后的迟到消息。</summary>
    public async Task StartAsync()
    {
        if (IsDisposed || IsRunning || IsComplete || _lifetime.IsCancellationRequested) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        IsRunning = true; IsComplete = false;
        Status = Text.Format("FolderStatisticsProgress", 0, 0, ObjectStoragePresentation.Size(0));
        RefreshCommands();
        bool IsCurrent() => !IsDisposed && !operation.IsCancellationRequested && ReferenceEquals(_operation, operation);
        var progress = new Progress<ObjectStorageFolderStatistics>(state =>
        {
            if (IsCurrent()) Status = Text.Format("FolderStatisticsProgress", state.Files, state.Folders, ObjectStoragePresentation.Size(state.Bytes));
        });
        try
        {
            var result = await _scan(progress, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (!IsCurrent()) return;
            _statistics = result;
            OnPropertyChanged(nameof(Size)); OnPropertyChanged(nameof(Contains)); OnPropertyChanged(nameof(Modified));
            IsComplete = true;
            Status = Text.Get("FolderStatisticsCompleted");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (!IsDisposed) Status = Text.Get("FolderStatisticsCanceled");
        }
        catch (Exception error)
        {
            if (IsCurrent()) Status = Text.Error(error, "FolderStatisticsFailed");
        }
        finally
        {
            _operation = null;
            if (!IsDisposed) { IsRunning = false; RefreshCommands(); }
        }
    }

    /// <summary>标题栏、Escape 和页面关闭均终止在途请求。</summary>
    public override void Dispose()
    {
        if (IsDisposed) return;
        base.Dispose();
        _lifetime.Cancel(); _lifetime.Dispose();
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        StartCommand.NotifyCanExecuteChanged(); StopCommand.NotifyCanExecuteChanged();
    }
}
