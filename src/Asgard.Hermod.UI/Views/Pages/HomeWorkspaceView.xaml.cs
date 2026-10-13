namespace Asgard.Hermod.UI.Views.Pages;

/// <summary>支持主题切换、键盘操作与窄窗口滚动的首页。</summary>
public partial class HomeWorkspaceView : UserControl
{
    /// <summary>初始化首页，数据与交互由工作页模型提供。</summary>
    public HomeWorkspaceView() => InitializeComponent();

    /// <summary>窄窗口优先给介绍与操作留出宽度，标志已由标题栏持续呈现。</summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        HeroLogo.Visibility = args.NewSize.Width >= 640 ? Visibility.Visible : Visibility.Collapsed;
        RecentDescription.Visibility = args.NewSize.Width >= 640 ? Visibility.Visible : Visibility.Collapsed;
        // 作者信息在窄窗口移到简介上方，避免与标题重叠。
        bool compact = args.NewSize.Width < 800;
        AuthorDetails.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        AuthorDetails.Margin = compact ? new Thickness(0, 30, 0, 8) : new Thickness();
    }
}
