namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>来源详情只展示固定文档的安全身份和快照信息，不读取远端数据。</summary>
public partial class SshLogSourceDetailsView : UserControl
{
    /// <summary>纯内容可直接离屏验证，不自行打开窗口或弹层。</summary>
    public SshLogSourceDetailsView() => InitializeComponent();
}
