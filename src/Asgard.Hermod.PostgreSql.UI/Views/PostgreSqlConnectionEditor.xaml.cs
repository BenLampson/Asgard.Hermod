using Asgard.Hermod.PostgreSql.UI.ViewModels;

namespace Asgard.Hermod.PostgreSql.UI.Views;

/// <summary>正文使用公共字段编辑器和密码控件，不复制连接生命周期。</summary>
public partial class PostgreSqlConnectionEditor : UserControl
{
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public PostgreSqlConnectionEditor(NewInstanceViewModel wizard)
    {
        InitializeComponent(); DataContext = new PostgreSqlConnectionEditorViewModel(wizard);
        Loaded += (_, _) => ((PostgreSqlConnectionEditorViewModel)DataContext).Attach();
        Unloaded += (_, _) => ((PostgreSqlConnectionEditorViewModel)DataContext).Dispose();
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs args)
    { base.OnGotKeyboardFocus(args); if (ReferenceEquals(args.NewFocus, this)) NameInput.Focus(); }
}
