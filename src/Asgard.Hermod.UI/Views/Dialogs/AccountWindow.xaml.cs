namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>账号窗口使用共享主题与原生键盘导航。</summary>
public partial class AccountWindow : Controls.HermodDialog
{
    /// <summary>窗口持有状态，服务和取消令牌由编辑器负责释放。</summary>
    public AccountWindow(AccountViewModel model)
    {
        InitializeComponent(); DataContext = model;
    }
}
