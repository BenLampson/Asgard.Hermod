namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>实例传输任务独立呈现，切换或关闭控制台不影响任务。</summary>
public partial class SshTransfersView : UserControl
{
    /// <summary>只读取现有任务，不自动发起传输。</summary>
    public SshTransfersView() => InitializeComponent();
}
