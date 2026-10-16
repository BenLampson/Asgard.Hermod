namespace Asgard.Hermod.UI.ViewModels;

/// <summary>先启动唯一初始化，再决定是否呈现；首次失败回收页面并向调用界面反馈。</summary>
public sealed partial class WorkspaceViewModel
{
    private readonly Dictionary<WorkspacePageViewModel, Task> _initializations = [];
    private readonly HashSet<WorkspacePageViewModel> _pendingActivations = [];
    /// <summary>首次连接留在主窗体，尚未创建工作窗口。</summary>
    public bool IsOpening => _pendingActivations.Count > 0;
    /// <summary>进度只包含安全实例名称。</summary>
    public string OpeningText => IsOpening ? Text.Format("WorkspaceOpeningInstance", _pendingActivations.First().Header) : "";
    /// <summary>取消当前显示的首次连接，迟到返回仍由模块释放。</summary>
    public RelayCommand CancelOpenCommand => field ??= new(_ => _pendingActivations.FirstOrDefault()?.ConnectionState?.CancelCommand.Execute(null));
    /// <summary>失败说明只包含实例显示名称和已经脱敏、本地化的原因。</summary>
    public string OpenError { get; private set; } = "";

    private void ActivatePage(WorkspacePageViewModel page)
    {
        if (page.IsClosed || IsExitPending || _disposeStarted) return;
        OpenError = ""; OnPropertyChanged(nameof(OpenError));
        if (_pendingActivations.Add(page)) _ = ActivateInitializedPageAsync(page);
    }
    private async Task ActivateInitializedPageAsync(WorkspacePageViewModel page)
    {
        RefreshOpening();
        try
        {
            await InitializePageAsync(page);
            if (!page.IsClosed && Tabs.Contains(page) && !IsExitPending && !_disposeStarted &&
                (page.HasConnectedOnce || page.InitialConnectionFailure is null)) PageActivated?.Invoke(page);
        }
        finally { _pendingActivations.Remove(page); RefreshOpening(); }
    }
    private void RefreshOpening()
    { OnPropertyChanged(nameof(IsOpening)); OnPropertyChanged(nameof(OpeningText)); }
    /// <summary>重复打开共享同一初始化任务；窗口关闭与迟到返回都不会重新添加页面。</summary>
    public Task InitializePageAsync(WorkspacePageViewModel page)
    {
        if (_initializations.TryGetValue(page, out var current)) return current;
        Task initialization = InitializePageCoreAsync(page);
        if (!page.IsClosed) _initializations.Add(page, initialization);
        return initialization;
    }
    private async Task InitializePageCoreAsync(WorkspacePageViewModel page)
    {
        string? failure;
        try { await page.InitializeAsync(); failure = page.InitialConnectionFailure; }
        catch (Exception error) { failure = Text.Error(error); }
        if (failure is null || page.HasConnectedOnce || page.IsClosed || !Tabs.Contains(page)) return;
        if (ReferenceEquals(SelectedTab, page)) SelectedTab = Home;
        Tabs.Remove(page); _instances.Remove(page); _initializations.Remove(page);
        page.NewSessionRequested -= OnNewSessionRequested; page.WorkspaceRequested -= OnWorkspaceRequested; page.CloseRequested -= OnPageCloseRequested;
        Task closing = page.CloseAsync(); _closingPages.Add(closing);
        try { await closing; }
        catch (Exception error) { if (failure.Length == 0) failure = Text.Error(error); }
        finally { _closingPages.Remove(closing); }
        if (!_disposeStarted && _disposeTask is null && !IsExitPending && failure.Length > 0)
        { OpenError = Text.Format("WorkspaceOpenFailed", page.Header, failure); OnPropertyChanged(nameof(OpenError)); }
    }
    private async void OnPageCloseRequested(object? sender, EventArgs args)
    {
        if (sender is not WorkspacePageViewModel page) return;
        try { await CloseTabAsync(page); }
        catch (Exception error) { OpenError = Text.Error(error); OnPropertyChanged(nameof(OpenError)); }
    }
}
