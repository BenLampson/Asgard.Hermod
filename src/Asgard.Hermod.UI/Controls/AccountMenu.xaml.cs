namespace Asgard.Hermod.UI.Controls;

/// <summary>
/// 账号入口仅呈现当前界面信息和已经接入的命令。
/// </summary>
public partial class AccountMenu : UserControl
{
    /// <summary>窄主窗体只展示完整头像按钮及下拉箭头，名称仍可由 tooltip 与无障碍读取。</summary>
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(AccountMenu), new PropertyMetadata(false));
    /// <summary>紧凑布局直接折叠文字，不裁剪可点击入口。</summary>
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    /// <summary>
    /// 创建账号菜单。
    /// </summary>
    public AccountMenu()
    {
        InitializeComponent();
        AccountButton.ContextMenu.CustomPopupPlacementCallback = (popup, target, _) =>
            [new(new Point(target.Width - popup.Width + 16, target.Height - 12), System.Windows.Controls.Primitives.PopupPrimaryAxis.Vertical)];
    }

    /// <summary>
    /// 左键打开菜单，同时保留原生菜单的键盘操作。
    /// </summary>
    private void OnAccountClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { IsSignedIn: true })
        {
            if (DataContext is ShellViewModel shell && shell.OpenAccountCommand.CanExecute(null))
                shell.OpenAccountCommand.Execute(null);
            return;
        }
        if (DataContext is ShellViewModel current && current.ResolveSyncConflictsCommand.CanExecute(null))
        {
            current.ResolveSyncConflictsCommand.Execute(null);
            return;
        }
        if (AccountButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = AccountButton;
            menu.IsOpen = true;
        }
    }

    /// <summary>未登录时右键或菜单键也不能展开仅适用于登录用户的菜单。</summary>
    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not ShellViewModel { IsSignedIn: true }) e.Handled = true;
    }
}
