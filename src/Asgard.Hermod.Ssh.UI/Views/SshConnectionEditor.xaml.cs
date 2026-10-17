using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>只负责 SSH 信息分区，所有字段仍使用公共表单和遮蔽控件。</summary>
public partial class SshConnectionEditor : UserControl
{
    /// <summary>绑定公共向导，不创建独立保存事务或内层滚动区。</summary>
    public SshConnectionEditor(NewInstanceViewModel wizard)
    {
        InitializeComponent();
        DataContext = new SshConnectionEditorViewModel(wizard);
        Loaded += (_, _) => ((SshConnectionEditorViewModel)DataContext).Attach();
        Unloaded += (_, _) => ((SshConnectionEditorViewModel)DataContext).Dispose();
    }

    /// <summary>公共向导将焦点交给正文时，转交名称编辑器。</summary>
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs args)
    {
        base.OnGotKeyboardFocus(args);
        if (ReferenceEquals(args.NewFocus, this)) SshNameInput.Focus();
    }
}
