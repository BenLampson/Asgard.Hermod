using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Views;

/// <summary>正文使用公共字段编辑器和密码控件，不复制连接生命周期。</summary>
public partial class MySqlConnectionEditor : UserControl
{
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public MySqlConnectionEditor(NewInstanceViewModel wizard)
    {
        InitializeComponent(); DataContext = new MySqlConnectionEditorViewModel(wizard);
        Loaded += (_, _) => ((MySqlConnectionEditorViewModel)DataContext).Attach();
        Unloaded += (_, _) => ((MySqlConnectionEditorViewModel)DataContext).Dispose();
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs args)
    { base.OnGotKeyboardFocus(args); if (ReferenceEquals(args.NewFocus, this)) NameInput.Focus(); }
}
