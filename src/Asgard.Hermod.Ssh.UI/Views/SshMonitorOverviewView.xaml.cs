namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>监测Overview页仅展示结构化数据，读取与生命周期由模型拥有。</summary>
public partial class SshMonitorOverviewView : UserControl
{
    /// <summary>加载共享主题和本地化模板。</summary>
    public SshMonitorOverviewView()
    {
        InitializeComponent();
        // 横向滚动容器会无限测量子级，必须用视口约束星号表格列和三等分曲线。
        SizeChanged += (_, _) => ConstrainContent();
    }
    private void OnOverviewScrollChanged(object sender, ScrollChangedEventArgs args) { if (args.ViewportWidthChange != 0) ConstrainContent(); }
    private void ConstrainContent()
    {
        double width = OverviewViewport.ViewportWidth > 0 ? OverviewViewport.ViewportWidth : ActualWidth;
        OverviewContent.Width = Math.Max(840, width - 48);
    }
}
