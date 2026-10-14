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
    }

    /// <summary>仅在当前没有键盘焦点时激活终端，保留搜索框和页签按钮的焦点。</summary>
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_disposed && TerminalView.Session is not null && Keyboard.FocusedElement is null)
            TerminalView.Focus();
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
        TerminalView.PreviewPaste -= OnPreviewPaste;
        BindingOperations.ClearBinding(TerminalView, WinTerminal.TerminalControl.SessionProperty);
        TerminalView.Session = null;
        TerminalView.Dispose();
        DataContext = null;
        GC.SuppressFinalize(this);
    }
}
