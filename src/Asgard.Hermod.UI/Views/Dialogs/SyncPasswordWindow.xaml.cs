namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>使用共享遮蔽控件索取单次同步密码，支持键盘导航和取消。</summary>
public partial class SyncPasswordWindow : Controls.HermodDialog
{
    private readonly SyncPasswordViewModel _model;

    /// <summary>绑定操作说明与确认状态，密码由调用方在弹窗结束后立即释放。</summary>
    public SyncPasswordWindow(SyncPasswordViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        InitializeComponent();
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>直接聚焦遮蔽编辑器，用户无需先点击密码输入框。</summary>
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        PasswordInput.ApplyTemplate();
        if (PasswordInput.Template.FindName("PART_Editor", PasswordInput) is PasswordBox editor)
            Keyboard.Focus(editor);
    }

    /// <summary>只返回用户明确确认的模态结果。</summary>
    private void OnCloseRequested(object? sender, bool accepted) => DialogResult = accepted;

    /// <summary>释放窗口事件，密码由调用方读取后清除。</summary>
    private void OnClosed(object? sender, EventArgs args)
    {
        _model.CloseRequested -= OnCloseRequested;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
    }
}
