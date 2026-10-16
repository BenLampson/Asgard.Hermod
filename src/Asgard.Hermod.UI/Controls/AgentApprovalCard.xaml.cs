namespace Asgard.Hermod.UI.Controls;

/// <summary>纯展示的内联确认卡，代码永远作为只读文本。</summary>
public partial class AgentApprovalCard : UserControl
{
    /// <summary>业务授权只由绑定命令处理，控件不连接数据库或 SSH。</summary>
    public AgentApprovalCard() => InitializeComponent();
}
