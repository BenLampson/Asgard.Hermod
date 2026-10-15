namespace Asgard.Hermod.UI.ViewModels;

/// <summary>先启动唯一初始化，再决定是否呈现；首次失败回收页面并向调用界面反馈。</summary>
public sealed partial class WorkspaceViewModel
{
    private readonly Dictionary<WorkspacePageViewModel, Task> _initializations = [];
    /// <summary>失败说明只包含实例显示名称和已经脱敏、本地化的原因。</summary>
    public string OpenError { get; private set; } = "";

    private void ActivatePage(WorkspacePageViewModel page)
    {
        if (page.IsClosed || IsExitPending || _disposeStarted) return;
        OpenError = ""; OnPropertyChanged(nameof(OpenError));
        _ = InitializePageAsync(page);
        // 同步失败可能已经移除了页面，不能在之后重新创建幽灵窗口。
        if (!page.IsClosed && Tabs.Contains(page) && !IsExitPending && _disposeTask is null)
            PageActivated?.Invoke(page);
    }
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
