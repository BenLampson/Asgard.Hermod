namespace Asgard.Hermod.UI.Views.Pages;

/// <summary>为已有原生终端控件提供页面布局；会话生命周期仍由视图模型管理。</summary>
public partial class TerminalWorkspaceView : UserControl, IDisposable
{
    private bool _disposed;

    /// <summary>初始化终端承载区并安排首次进入页面时的合理焦点。</summary>
    public TerminalWorkspaceView()
    {
        InitializeComponent();
        TerminalView.PreviewPaste += OnPreviewPaste;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>终端页显示后把键盘焦点交给终端，确保输入法和数字键进入同一输入上下文。</summary>
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed || TerminalView.Session is null)
            return;
        if (Window.GetWindow(this) is { } window)
        {
            window.Activated -= OnOwnerActivated;
            window.Activated += OnOwnerActivated;
            if (window.IsActive)
                FocusTerminal();
        }
    }

    /// <summary>窗口首次激活晚于页面加载时重新建立终端键盘焦点。</summary>
    private void OnOwnerActivated(object? sender, EventArgs args) => FocusTerminal();

    /// <summary>页面卸载时解除宿主窗口事件，避免缓存页继续抢焦点。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (Window.GetWindow(this) is { } window)
            window.Activated -= OnOwnerActivated;
    }

    /// <summary>在窗口输入队列稳定后聚焦终端，使输入法上下文和预处理订阅同时建立。</summary>
    private void FocusTerminal()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!_disposed && IsVisible && TerminalView.Session is not null)
                TerminalView.Focus();
        }));
    }

    /// <summary>本地终端与 SSH 复用相同的多行粘贴确认语义。</summary>
    private void OnPreviewPaste(object? sender, WinTerminal.TerminalPasteEventArgs args)
    {
        if (!args.Text.Contains('\n') && !args.Text.Contains('\r')) return;
        var dialog = new Dialogs.ConfirmationDialog(Text.Get("SshPasteTitle"), Text.Get("SshPasteDescription"), Text.Get("SshPasteConfirm"), preview: args.Text)
        { Owner = Window.GetWindow(this) };
        args.Cancel = dialog.ShowDialog() != true;
    }

    /// <summary>关闭页签时解除会话绑定并释放控件；切换页签的 Unloaded 不执行此操作。</summary>
    public void Dispose()
    {
        VerifyAccess();
        if (_disposed)
            return;
        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        if (Window.GetWindow(this) is { } window)
            window.Activated -= OnOwnerActivated;
        TerminalView.PreviewPaste -= OnPreviewPaste;
        BindingOperations.ClearBinding(TerminalView, WinTerminal.TerminalControl.SessionProperty);
        TerminalView.Session = null;
        TerminalView.Dispose();
        DataContext = null;
        GC.SuppressFinalize(this);
    }
}
