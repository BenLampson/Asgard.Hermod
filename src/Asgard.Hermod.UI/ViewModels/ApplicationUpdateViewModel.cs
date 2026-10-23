namespace Asgard.Hermod.UI.ViewModels;

/// <summary>主窗口和设置共享的更新状态；后台只检查，安装始终由用户在分发页面操作。</summary>
public sealed class ApplicationUpdateViewModel : ObservableObject, IDisposable
{
    private readonly IApplicationUpdateService _service;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _started;
    private bool _disposed;
    private bool _isChecking;
    private bool _hasUpdate;
    private string _status = Text.Get("UpdatesNotChecked");
    private PublishedApplicationRelease? _release;

    /// <summary>构造不发起网络请求；应用显示后才启动定时检查。</summary>
    public ApplicationUpdateViewModel(IApplicationUpdateService service)
    {
        _service = service;
        CheckCommand = new AsyncRelayCommand(_ => CheckAsync(), _ => !_disposed && !IsChecking);
        OpenDownloadCommand = new RelayCommand(_ =>
        {
            try { _service.OpenDownloadPage(); }
            catch (Exception) { Status = Text.Get("UpdatesOpenFailed"); }
        }, _ => !_disposed);
    }

    /// <summary>当前实际运行的安装版本。</summary>
    public string CurrentVersion => Text.Format("UpdatesCurrentVersion", _service.CurrentVersion.ToString());
    /// <summary>公开发布版本与商店可用性分开展示。</summary>
    public string PublishedVersion => _release is null ? Text.Get("UpdatesReleaseUnavailable") : Text.Format("UpdatesPublishedVersion", _release.Version.ToString());
    /// <summary>外部发布正文作为纯文本显示；缺失时提供本地化说明。</summary>
    public string ReleaseNotes => string.IsNullOrWhiteSpace(_release?.Notes) ? Text.Get("UpdatesNotesUnavailable") : _release.Notes;
    /// <summary>商店审核和分批推送可能晚于 GitHub 发布。</summary>
    public string ChannelHint => Text.Get(_service.UsesStore ? "UpdatesStoreHint" : "UpdatesGitHubHint");
    /// <summary>只打开页面，不在应用内触发安装。</summary>
    public string OpenDownloadLabel => Text.Get(_service.UsesStore ? "UpdatesOpenStore" : "UpdatesOpenGitHub");
    /// <summary>最后一次检查状态，错误不泄露远程响应。</summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>检查期间禁用重复请求。</summary>
    public bool IsChecking { get => _isChecking; private set { SetProperty(ref _isChecking, value); CheckCommand.NotifyCanExecuteChanged(); } }
    /// <summary>只在确认发现新版本后显示主窗口提示；临时失败不清除已知更新。</summary>
    public bool HasUpdate { get => _hasUpdate; private set => SetProperty(ref _hasUpdate, value); }
    /// <summary>设置页手动检查入口。</summary>
    public AsyncRelayCommand CheckCommand { get; }
    /// <summary>由用户主动打开官方分发页面。</summary>
    public RelayCommand OpenDownloadCommand { get; }

    /// <summary>启动后延迟检查，随后每六小时查询；不阻塞工作区初始化。</summary>
    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _ = RunAsync(_lifetime.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                await CheckAsync();
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    /// <summary>串行且有时限的只读查询；应用关闭后不再发布迟到状态。</summary>
    public async Task CheckAsync()
    {
        if (_disposed || IsChecking) return;
        IsChecking = true;
        Status = Text.Get("UpdatesChecking");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var result = await _service.CheckAsync(timeout.Token).WaitAsync(timeout.Token);
            if (_disposed) return;
            HasUpdate = result.HasUpdate;
            _release = result.Release;
            OnPropertyChanged(nameof(PublishedVersion));
            OnPropertyChanged(nameof(ReleaseNotes));
            Status = Text.Get(HasUpdate ? "UpdatesAvailable" : "UpdatesCurrent");
        }
        catch (Exception) { if (!_disposed) Status = Text.Get("UpdatesCheckFailed"); }
        finally { if (!_disposed) IsChecking = false; }
    }

    /// <summary>关闭程序时取消检查，不等待网络或更改连接生命周期。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        CheckCommand.NotifyCanExecuteChanged();
        OpenDownloadCommand.NotifyCanExecuteChanged();
    }
}
