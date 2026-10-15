using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod;

/// <summary>协调主窗体托盘隐藏、独立工作窗口预检及全应用异步退出。</summary>
public partial class MainWindow : HermodWindow
{
    private readonly ShellViewModel _model;
    private readonly WorkspaceWindowHost? _workWindows;
    private readonly ITrayIcon? _tray;
    private readonly ITrayPreferenceStore? _preferences;
    private readonly Func<Task>? _finishExit;
    private readonly Func<int, Task<bool>>? _confirmExit;
    private bool _closing;
    private bool _closeApproved;
    private bool _hintShown;
    private bool _trayFailureReported;
    private TrayMenuWindow? _menu;
    private TaskCompletionSource? _exitFinished;
    private bool _sessionEnding;
    /// <summary>全部预检通过后通知宿主停止接受重复激活。</summary>
    public event Action? ExitCommitted;

    /// <summary>主进程装配关闭偏好；未装配该能力的轻量宿主保留原有关闭行为。</summary>
    public MainWindow(ShellViewModel model, WorkspaceWindowHost? workWindows = null, ITrayIcon? tray = null,
        ITrayPreferenceStore? preferences = null, Func<Task>? finishExit = null, Func<int, Task<bool>>? confirmExit = null,
        IMainWindowClosePreferenceStore? closePreferences = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model; _workWindows = workWindows; _tray = tray; _preferences = preferences;
        _finishExit = finishExit; _confirmExit = confirmExit;
        InitializeComponent(); DataContext = model;
        InitializeClosePreferences(closePreferences);
        WindowCloseBehavior.SetHideToTray(this, tray?.IsAvailable == true);
        model.Sidebar.AddInstanceCommand.ExecutionFailed += OnCommandFailed;
        model.Workspace.CloseTabCommand.ExecutionFailed += OnCommandFailed;
        model.SetLightThemeCommand.ExecutionFailed += OnCommandFailed;
        model.SetDarkThemeCommand.ExecutionFailed += OnCommandFailed;
        model.ToggleThemeCommand.ExecutionFailed += OnCommandFailed;
        Closing += OnWindowClosing;
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                if (ShellView.FindName("MainContent") is Asgard.Hermod.UI.Views.Shell.MainWorkspaceView view) view.FocusSearch();
                args.Handled = true;
            }
        };
        if (tray is not null)
        {
            tray.Activated += ActivateMain; tray.MenuRequested += OpenTrayMenu;
            tray.AvailabilityChanged += OnTrayAvailabilityChanged;
            Loaded += (_, _) => OnTrayAvailabilityChanged();
        }
    }

    /// <summary>所有窗口已同意并进入资源释放后，不再响应重复激活。</summary>
    public bool IsExitCommitted { get; private set; }

    /// <summary>在窗口真实 DPI 下限制默认尺寸。</summary>
    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);
        if (WindowStartupLocation == WindowStartupLocation.CenterScreen) WindowStartupPlacement.Apply(this);
    }

    /// <summary>IPC 与托盘共用恢复逻辑，模态对话框仍保留当前输入。</summary>
    public void ActivateMain()
    {
        if (IsExitCommitted) return;
        WindowActivation.Show(this);
    }

    /// <summary>只有可用托盘才隐藏，立即记录一次性提示；不触碰工作区和后台服务。</summary>
    public void HideToTray()
    {
        if (_closing || IsExitCommitted || _tray?.IsAvailable != true) { ActivateMain(); return; }
        if (OwnedWindows.Cast<Window>().Any(window => window.IsVisible)) { ActivateMain(); return; }
        try
        {
            bool showHint = !_hintShown && _preferences?.HasShownHideHint() != true;
            if (showHint) _preferences?.MarkHideHintShown();
            _hintShown = true;
            Hide(); ShowInTaskbar = false;
            if (showHint) _tray.ShowHiddenHint();
        }
        catch (Exception error) { ActivateMain(); OnCommandFailed(this, error); }
    }

    /// <summary>注册失败时恢复可访问主窗体；重复失败不反复打扰用户。</summary>
    private void OnTrayAvailabilityChanged()
    {
        WindowCloseBehavior.SetHideToTray(this, _tray?.IsAvailable == true);
        UpdateClosePresentation();
        if (_closePrompt is not null) { _closePrompt.SetTrayAvailable(_tray?.IsAvailable == true); if (_tray?.IsAvailable != true) ActivateMain(); return; }
        if (_tray?.IsAvailable == true) { _trayFailureReported = false; return; }
        if (IsExitCommitted) return;
        ActivateMain(); _menu?.Close();
        if (_trayFailureReported) return;
        _trayFailureReported = true;
        new MessageDialog(Text.Get("Hermod"), Text.Get("TrayUnavailable")) { Owner = this }.ShowDialog();
    }

    /// <summary>菜单仅作用于主窗体；定位失败也不能把用户留在不可访问状态。</summary>
    private void OpenTrayMenu()
    {
        if (IsExitCommitted || _closing || _tray is null) return;
        if (!_tray.TryGetBounds(out var rectangle)) { ActivateMain(); return; }
        _menu?.Close();
        bool show = !IsVisible || WindowState == WindowState.Minimized;
        _menu = new TrayMenuWindow(show, show ? ActivateMain : HideToTray, () => _ = RequestExitAsync());
        _menu.ShowAt(rectangle);
    }

    /// <summary>真实交互错误仅展示安全资源，绝不泄漏连接信息或 SDK 响应。</summary>
    private void OnCommandFailed(object? sender, Exception exception) =>
        new MessageDialog(Text.Get("Hermod"), Text.Error(exception)) { Owner = DialogOwner.Current ?? this }.ShowDialog();

    /// <summary>关闭按钮及 Alt+F4 共用偏好驱动的关闭流程；最小化不经过关闭事件。</summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs args)
    {
        if (_closeApproved || _sessionEnding) return;
        args.Cancel = true;
        if (_closePreferences is not null) { ScheduleMainClose(); return; }
        if (_tray?.IsAvailable == true) HideToTray();
        else _ = RequestExitAsync();
    }

    /// <summary>全应用退出先预检所有窗口，再提交关闭决定并等待所有异步清理。</summary>
    public async Task<bool> RequestExitAsync(bool sessionEnding = false)
    {
        if (sessionEnding) _closePrompt?.Close();
        if (sessionEnding) { _sessionEnding = true; WindowCloseBehavior.SetHideToTray(this, false); }
        if (!sessionEnding && WindowActivation.FindModal() is { } modal)
        {
            WindowActivation.Show(modal);
            return false;
        }
        if (!sessionEnding && _workWindows?.Windows.FirstOrDefault(window => window.IsClosePending) is { } pending)
        {
            WindowActivation.Show(pending);
            return false;
        }
        if (sessionEnding && _closing && _exitFinished is not null)
        {
            await _exitFinished.Task;
            if (IsExitCommitted) return true;
        }
        if (_closing || IsExitCommitted) return false;
        _closing = true;
        _exitFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows = _workWindows?.Windows.ToArray() ?? [];
        _model.Workspace.IsExitPending = true;
        ShellView.IsEnabled = false;
        if (CaptionContent is UIElement caption) caption.IsEnabled = false;
        foreach (var window in windows)
        {
            window.IsExitPending = true;
            if (window.Content is UIElement content) content.IsEnabled = false;
        }
        try
        {
            var plans = new List<WorkspaceClosePlan>();
            if (!sessionEnding)
            {
                int count = _model.Workspace.Tabs.Count(page => page.CanClose) + _model.Workspace.PendingCloseCount;
                if (_workWindows is not null && count > 0 && !await ConfirmExitAsync(count)) return false;
                foreach (var page in _model.Workspace.Tabs.ToArray())
                {
                    DialogOwner.Override = windows.FirstOrDefault(window => ReferenceEquals(window.Page, page)) ?? (Window)this;
                    var plan = await page.PrepareCloseAsync();
                    if (plan is null) return false;
                    plans.Add(plan);
                }
            }
            IsExitCommitted = true;
            ExitCommitted?.Invoke();
            _menu?.Close();
            foreach (var plan in plans) await plan.CommitAsync();
            await _model.DisposeAsync();
            await FinishExitAsync();
            return true;
        }
        catch (Exception error)
        {
            if (!sessionEnding) OnCommandFailed(this, error);
            if (IsExitCommitted)
            {
                try { await _model.Workspace.DisposeAsync(); } catch (Exception) { }
                await FinishExitAsync();
            }
            return false;
        }
        finally
        {
            DialogOwner.Override = null;
            _exitFinished.TrySetResult();
            if (!IsExitCommitted)
            {
                _closing = false; _model.Workspace.IsExitPending = false; ShellView.IsEnabled = true;
                if (CaptionContent is UIElement restoredCaption) restoredCaption.IsEnabled = true;
                foreach (var window in windows)
                {
                    window.IsExitPending = false;
                    if (window.Content is UIElement content) content.IsEnabled = true;
                }
            }
        }
    }

    /// <summary>存在工作窗口或任务时明确显示影响，取消为默认焦点。</summary>
    private Task<bool> ConfirmExitAsync(int count)
    {
        if (_confirmExit is not null) return _confirmExit(count);
        ActivateMain();
        return Task.FromResult(new ConfirmationDialog(Text.Get("ExitHermod"), Text.Format("ExitHermodImpact", count),
            Text.Get("ExitHermod"), danger: true)
        { Owner = this }.ShowDialog() == true);
    }

    /// <summary>资源释放完成后移除托盘，关闭 UI，最后由宿主释放 IPC、所有权和进程。</summary>
    private async Task FinishExitAsync()
    {
        if (_closePreferences is not null) _closePreferences.Changed -= OnClosePreferencesChanged;
        _model.Sidebar.AddInstanceCommand.ExecutionFailed -= OnCommandFailed;
        _model.Workspace.CloseTabCommand.ExecutionFailed -= OnCommandFailed;
        _model.SetLightThemeCommand.ExecutionFailed -= OnCommandFailed;
        _model.SetDarkThemeCommand.ExecutionFailed -= OnCommandFailed;
        _model.ToggleThemeCommand.ExecutionFailed -= OnCommandFailed;
        ShellView.Dispose(); _workWindows?.Dispose();
        if (_tray is not null)
        {
            _tray.Activated -= ActivateMain; _tray.MenuRequested -= OpenTrayMenu;
            _tray.AvailabilityChanged -= OnTrayAvailabilityChanged; _tray.Dispose();
        }
        _closeApproved = true;
        await Dispatcher.InvokeAsync(() => Close(), System.Windows.Threading.DispatcherPriority.Background);
        if (_finishExit is not null) await _finishExit();
    }
}
