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
        DataContextChanged += OnModelChanged;
        Loaded += OnLoaded;
        PreviewKeyDown += OnKeyDown;
        TerminalView.ScreenUpdated += OnTerminalUpdated;
    }
    /// <summary>从旧页面解除订阅，绑定新页面的详情显隐。</summary>
    private void OnModelChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = args.NewValue as SshWorkspaceViewModel;
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
        UpdateLayoutMode();
    }
    /// <summary>内容宿主与外壳都可触发初始化，模型保证只连接一次。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_model is not null) await _model.InitializeAsync();
        if (!_disposed) { UpdateLayoutMode(); TerminalView.Focus(); }
    }
    /// <summary>详情面板变更需要同步 PTY 网格；终端会自行报告新尺寸。</summary>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SshWorkspaceViewModel.IsDetailsOpen)) UpdateLayoutMode();
    }
    /// <summary>窄窗口把次要动作收进更多，详情改为覆盖式面板。</summary>
    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args) => UpdateLayoutMode();
    /// <summary>窗口缩小时保留连接状态和主操作，避免横向溢出。</summary>
    private void UpdateLayoutMode()
    {
        if (DetailsColumn is null) return;
        bool wide = ActualWidth >= 760;
        bool open = _model?.IsDetailsOpen == true;
        DetailsColumn.Width = open && wide ? new GridLength(300) : new GridLength(0);
        Grid.SetColumn(DetailsPanel, wide ? 1 : 0);
        Grid.SetColumnSpan(DetailsPanel, wide ? 1 : 2);
        DetailsPanel.HorizontalAlignment = HorizontalAlignment.Right;
        CopyAction.Visibility = PasteAction.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        DetailsActionLabel.Visibility = ActualWidth >= 900 ? Visibility.Visible : Visibility.Collapsed;
        FooterRoute.Visibility = FooterTerminalType.Visibility = ActualWidth >= 600 ? Visibility.Visible : Visibility.Collapsed;
        SearchOverlay.Width = Math.Max(240, Math.Min(484, TerminalSurface.ActualWidth - 28));
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
        if (args.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { OpenSearch(); args.Handled = true; }
        else if (SearchOverlay.IsVisible && args.Key == Key.Escape) { CloseSearch(); args.Handled = true; }
        else if (ReferenceEquals(args.OriginalSource, SearchInput) && args.Key == Key.Enter)
        { Search(!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); args.Handled = true; }
    }
    /// <summary>打开搜索不会向服务器发送任何命令。</summary>
    private void OnSearchClick(object sender, RoutedEventArgs args) => OpenSearch();
    /// <summary>给搜索框明确焦点，键盘输入不落入远端 shell。</summary>
    private void OpenSearch() { SearchOverlay.Visibility = Visibility.Visible; SearchInput.Focus(); SearchInput.SelectAll(); }
    /// <summary>关闭搜索后恢复终端焦点。</summary>
    private void OnCloseSearch(object sender, RoutedEventArgs args) => CloseSearch();
    /// <summary>清除搜索选区，但不清空终端输出。</summary>
    private void CloseSearch() { SearchOverlay.Visibility = Visibility.Collapsed; TerminalView.FindText(""); TerminalView.Focus(); }
    /// <summary>搜索内容变化后选择第一个匹配。</summary>
    private void OnSearchChanged(object sender, TextChangedEventArgs args) { if (TerminalView is not null) Search(true); }
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
        Add("SshIncreaseFont", new RelayCommand(_ => TerminalView.FontSize = Math.Min(28, TerminalView.FontSize + 1)));
        Add("SshDecreaseFont", new RelayCommand(_ => TerminalView.FontSize = Math.Max(9, TerminalView.FontSize - 1)));
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
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        if (MoreAction.ContextMenu is { } menu) { menu.IsOpen = false; menu.Items.Clear(); }
        TerminalView.ScreenUpdated -= OnTerminalUpdated;
        TerminalView.Dispose(); DataContextChanged -= OnModelChanged; Loaded -= OnLoaded; PreviewKeyDown -= OnKeyDown;
    }
}
