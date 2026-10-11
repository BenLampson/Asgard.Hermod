namespace Asgard.Hermod.UI.Views.Shell;

/// <summary>
/// 组合实例导航与工作区，管理内容区域的界面生命周期。
/// </summary>
public partial class AppShellView : UserControl, IDisposable
{
    /// <summary>
    /// 创建外壳组件。
    /// </summary>
    public AppShellView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 收起侧栏的搜索入口先展开侧栏，再把焦点交给过滤框。
    /// </summary>
    private void OnExpandSearchRequested(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell && !shell.IsSidebarExpanded
            && shell.ToggleSidebarCommand.CanExecute(null))
        {
            shell.ToggleSidebarCommand.Execute(null);
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => SidebarView.FocusSearch());
        e.Handled = true;
    }

    /// <summary>
    /// 释放工作区视图；会话仍由宿主与对应的 ViewModel 管理。
    /// </summary>
    public void Dispose()
    {
        SidebarView.CloseTagPicker();
        Workspace.Dispose();
        GC.SuppressFinalize(this);
    }
}
