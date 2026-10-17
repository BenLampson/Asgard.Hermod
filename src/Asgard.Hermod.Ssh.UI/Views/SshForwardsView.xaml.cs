namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>实例级转发列表和新建表单，不与控制台生命周期绑定。</summary>
public partial class SshForwardsView : UserControl
{
    /// <summary>操作只复用实例提供的转发命令。</summary>
    public SshForwardsView() => InitializeComponent();
}
