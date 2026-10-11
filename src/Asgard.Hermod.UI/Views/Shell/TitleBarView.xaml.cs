namespace Asgard.Hermod.UI.Views.Shell;

/// <summary>
/// 显示应用标识、全局搜索、账号入口和原生窗口按钮。
/// </summary>
public partial class TitleBarView : UserControl
{
    /// <summary>
    /// 创建标题栏。
    /// </summary>
    public TitleBarView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 左键打开设置菜单，其命令继续由绑定提供。
    /// </summary>
    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (SettingsButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = SettingsButton;
            menu.IsOpen = true;
        }
    }
}
