using System.Runtime.InteropServices;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>终端 UI 只负责焦点、剪贴板、搜索和响应式布局，协议由页面模型拥有。</summary>
public partial class SshWorkspaceView : UserControl, IDisposable
{
    private SshWorkspaceViewModel? _model;
    private bool _disposed;

    /// <summary>缓存视图切换不会释放会话，真正关闭时由内容宿主调用 Dispose。</summary>
    public SshWorkspaceView()
    {
        InitializeComponent();
        InitializeMonitorResizeTracking();
        InitializeToolboxResizeTracking();
        SessionTools.FocusTarget = TerminalView;
        DataContextChanged += OnModelChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PreviewKeyDown += OnKeyDown;
        TerminalView.ScreenUpdated += OnTerminalUpdated;
    }
    /// <summary>读取当前协议模型；详情显隐直接绑定公共布局，不订阅后重画布局。</summary>
    private void OnModelChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (_model is not null) _model.LogWindowRequested -= OnLogWindowRequested;
        if (_model is not null) _model.DockerCommands.InsertRequested -= OnDockerInsert;
        CloseLogWindows();
        foreach (var assistant in _openedAssistants) assistant.InsertRequested -= OnAgentInsert; _openedAssistants.Clear();
        if (_model is not null) _model.PropertyChanged -= OnWorkspacePropertyChanged;
        _model = args.NewValue as SshWorkspaceViewModel;
        if (_model is not null) _model.LogWindowRequested += OnLogWindowRequested;
        if (_model is not null) _model.DockerCommands.InsertRequested += OnDockerInsert;
        if (_model is not null) _model.PropertyChanged += OnWorkspacePropertyChanged;
        ConfigureTools();
        UpdateLayoutMode();
    }
    /// <summary>内容宿主与外壳都可触发初始化，模型保证只连接一次。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (Window.GetWindow(this) is { } window)
        {
            window.Activated -= OnOwnerActivated;
            window.Activated += OnOwnerActivated;
        }
        if (_model is not null) await _model.InitializeAsync();
        if (!_disposed) { UpdateLayoutMode(); FocusTerminal(); }
    }
    /// <summary>窗口首次激活晚于 SSH 页面加载时重新建立终端键盘焦点。</summary>
    private void OnOwnerActivated(object? sender, EventArgs args) => FocusTerminal();
    /// <summary>页面卸载时解除窗口事件，避免缓存页面抢占其他页面焦点。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (Window.GetWindow(this) is { } window)
            window.Activated -= OnOwnerActivated;
    }
    /// <summary>等待窗口输入队列稳定后聚焦终端，确保数字键和输入法进入终端上下文。</summary>
    private void FocusTerminal()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!_disposed && IsVisible && _model?.IsTerminalSection == true && !_model.IsAgentOpen && TerminalView.Session is not null)
                TerminalView.Focus();
        }));
    }
    /// <summary>窄窗口把次要动作收进更多；详情布局由公共控件负责。</summary>
    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args) => UpdateLayoutMode();
    /// <summary>翻译后的动作宽度改变时重新分配标题空间，不用固定英文宽度猜测。</summary>
    private void OnToolbarActionsSizeChanged(object sender, SizeChangedEventArgs args) => UpdateLayoutMode();
    /// <summary>窗口缩小时保留连接状态和主操作，避免横向溢出。</summary>
    private void UpdateLayoutMode()
    {
        if (WorkspaceLayout is null) return;
        UpdateMonitorLayout();
        UpdateToolboxLayout();
        if (MonitorActionLabel is not null) MonitorActionLabel.Visibility = ActualWidth >= 1000 ? Visibility.Visible : Visibility.Collapsed;
        if (DockerActionLabel is not null) DockerActionLabel.Visibility = ActualWidth >= 1150 ? Visibility.Visible : Visibility.Collapsed;
        FooterRoute.Visibility = FooterTerminalType.Visibility = ActualWidth >= 800 ? Visibility.Visible : Visibility.Collapsed;

        ResizeSearchOverlay();
    }
    /// <summary>公共详情栏展开会改变正文宽度，搜索浮层跟随实际终端空间。</summary>
    private void OnTerminalSizeChanged(object sender, SizeChangedEventArgs args) => ResizeSearchOverlay();
    /// <summary>浮层宽度受终端视口限制，避免在详情切换后沿用旧窗口尺寸。</summary>
    private void ResizeSearchOverlay()
    {
        if (SearchOverlay is not null) SearchOverlay.Width = Math.Max(0, Math.Min(484, TerminalSurface.ActualWidth - 28));
    }
    /// <summary>复制仅操作当前选区，不改变远端状态。</summary>
    private void OnCopyClick(object sender, RoutedEventArgs args) { TerminalView.CopySelection(); TerminalView.Focus(); }
    /// <summary>所有粘贴入口经过终端的共用确认事件。</summary>
    private void OnPasteClick(object sender, RoutedEventArgs args) { TerminalView.Paste(); TerminalView.Focus(); }
    /// <summary>多行粘贴先显示原始文本；取消不发送任何内容。</summary>
    private void OnPreviewPaste(object? sender, TerminalPasteEventArgs args)
    {
        if (!args.Text.Contains('\n') && !args.Text.Contains('\r')) return;
        var dialog = new ConfirmationDialog(Text.Get("SshPasteTitle"), Text.Get("SshPasteDescription"), Text.Get("SshPasteConfirm"), preview: args.Text)
        { Owner = Window.GetWindow(this) };
        args.Cancel = dialog.ShowDialog() != true;
    }
    /// <summary>在终端接收 Ctrl+F 前打开本机输出搜索。</summary>
    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { OpenSearch(); args.Handled = true; }
        else if (SearchOverlay.IsVisible && args.Key == Key.Escape) { CloseSearch(); args.Handled = true; }
        else if (ReferenceEquals(args.OriginalSource, SearchInput) && args.Key == Key.Enter)
        { Search(!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); args.Handled = true; }
    }
    /// <summary>打开搜索不会向服务器发送任何命令。</summary>
    private void OnSearchClick(object sender, RoutedEventArgs args) => OpenSearch();
    /// <summary>给搜索框明确焦点，键盘输入不落入远端 shell。</summary>
    private void OpenSearch() { if (_model?.ActiveConsole is not { } console) return; console.IsSearchOpen = true; SessionTools.Close(false); SearchInput.Focus(); SearchInput.SelectAll(); }
    /// <summary>关闭搜索后恢复终端焦点。</summary>
    private void OnCloseSearch(object sender, RoutedEventArgs args) => CloseSearch();
    /// <summary>清除搜索选区，但不清空终端输出。</summary>
    private void CloseSearch() { if (_model?.ActiveConsole is { } console) console.IsSearchOpen = false; TerminalView.Focus(); }
    /// <summary>搜索内容变化后选择第一个匹配。</summary>
    private void OnSearchChanged(object sender, TextChangedEventArgs args) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!_disposed && _model?.Session is { } session && ReferenceEquals(session, TerminalView.Session)) Search(true, false); }));
    /// <summary>选择前一个匹配。</summary>
    private void OnPreviousMatch(object sender, RoutedEventArgs args) => Search(false);
    /// <summary>选择后一个匹配。</summary>
    private void OnNextMatch(object sender, RoutedEventArgs args) => Search(true);
    /// <summary>计数整句来自共享资源，原始输出不翻译。</summary>
    private void Search(bool forward, bool advance = true)
    {
        var result = TerminalView.FindText(SearchInput.Text, forward, advance);
        MatchCount.Text = Text.Format("SshSearchMatches", result.Index, result.Count);
    }
    /// <summary>输出或网格变化后更新本机匹配结果，保持当前匹配且不跳到下一条。</summary>
    private void OnTerminalUpdated(object? sender, EventArgs args)
    {
        if (SearchOverlay.IsVisible) Search(true, false);
    }
    /// <summary>只复制已展示的公开主机指纹。</summary>
    private void OnCopyFingerprint(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string fingerprint }) return;
        try { Clipboard.SetText(fingerprint); } catch (ExternalException) { }
    }
    /// <summary>菜单复用公共样式，窄窗口仍能操作复制和粘贴。</summary>
    private void OnMoreClick(object sender, RoutedEventArgs args)
    {
        var menu = new ContextMenu { PlacementTarget = MoreAction };
        Add("SshNewSession", _model?.NewSessionCommand);
        Add("SshCopySelection", new RelayCommand(_ => TerminalView.CopySelection()));
        Add("SshPaste", new RelayCommand(_ => TerminalView.Paste(), _ => _model?.IsConnected == true));
        menu.Items.Add(new Separator());
        Add("SshIncreaseFont", _increaseFont);
        Add("SshDecreaseFont", _decreaseFont);
        Add("SshClearDisplay", new RelayCommand(_ => TerminalView.ClearDisplay()));
        menu.Items.Add(new Separator());
        Add("SshDisconnect", _model?.DisconnectCommand);
        MoreAction.ContextMenu = menu; menu.IsOpen = true;
        void Add(string key, ICommand? command) => menu.Items.Add(new MenuItem { Header = Text.Get(key), Command = command });
    }
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_model is not null) _model.DockerCommands.InsertRequested -= OnDockerInsert;
        if (_model is not null) _model.LogWindowRequested -= OnLogWindowRequested;
        CloseLogWindows();
        StopMonitorResizeTracking();
        StopToolboxResizeTracking();
        foreach (var assistant in _openedAssistants) assistant.InsertRequested -= OnAgentInsert; _openedAssistants.Clear();
        if (_model is not null) _model.PropertyChanged -= OnWorkspacePropertyChanged;
        SessionTools.Close(false);
        if (SessionTools.ContextMenu is { } toolsMenu) { toolsMenu.IsOpen = false; toolsMenu.Items.Clear(); }
        if (MoreAction.ContextMenu is { } menu) { menu.IsOpen = false; menu.Items.Clear(); }
        TerminalView.ScreenUpdated -= OnTerminalUpdated;
        TerminalView.Dispose(); DataContextChanged -= OnModelChanged; Loaded -= OnLoaded; Unloaded -= OnUnloaded; PreviewKeyDown -= OnKeyDown;
        if (Window.GetWindow(this) is { } window)
            window.Activated -= OnOwnerActivated;
    }
}
