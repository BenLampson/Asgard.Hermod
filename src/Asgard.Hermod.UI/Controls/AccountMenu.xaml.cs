namespace Asgard.Hermod.UI.Controls;

/// <summary>
/// 账号入口仅呈现当前界面信息和已经接入的命令。
/// </summary>
public partial class AccountMenu : UserControl
{
    /// <summary>
    /// 创建账号菜单。
    /// </summary>
    public AccountMenu()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 左键打开菜单，同时保留原生菜单的键盘操作。
    /// </summary>
    private void OnAccountClick(object sender, RoutedEventArgs e)
    {
        if (AccountButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = AccountButton;
            menu.IsOpen = true;
        }
    }
}
