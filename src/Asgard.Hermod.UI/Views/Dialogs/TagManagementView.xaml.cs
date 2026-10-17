namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>封装标签管理的树、搜索、详情和边界状态，系统设置只负责导航。</summary>
public partial class TagManagementView : UserControl
{
    /// <summary>创建响应明暗主题的管理面板。</summary>
    public TagManagementView() => InitializeComponent();

    /// <summary>菜单打开前选择真实行，所有写操作仍由注入服务执行。</summary>
    private void OnTagMenuButtonClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: ManagedTagNodeViewModel node, ContextMenu: { } menu } button)
            return;
        node.SelectCommand.Execute(null);
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        args.Handled = true;
    }
}
