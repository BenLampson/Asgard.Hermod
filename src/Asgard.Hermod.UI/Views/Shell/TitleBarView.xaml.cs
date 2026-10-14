namespace Asgard.Hermod.UI.Views.Shell;

/// <summary>固定页签与品牌属于主窗体标题栏，空白区域保留原生拖动。</summary>
public partial class TitleBarView : UserControl
{
    /// <summary>从 Shell 绑定真实账号、设置和工作台导航。</summary>
    public TitleBarView() => InitializeComponent();
    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        BrandName.Visibility = ActualWidth < 1160 ? Visibility.Collapsed : Visibility.Visible;
        Account.IsCompact = ActualWidth < 1160;
    }
}
