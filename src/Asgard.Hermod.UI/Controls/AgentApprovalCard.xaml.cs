namespace Asgard.Hermod.UI.Controls;

/// <summary>纯展示的内联确认卡，代码永远作为只读文本。</summary>
public partial class AgentApprovalCard : UserControl
{
    /// <summary>嵌入 SQL 建议时复用外层完整代码阅读区，避免重复展示。</summary>
    public static readonly DependencyProperty ShowCodeProperty = DependencyProperty.Register(nameof(ShowCode), typeof(bool), typeof(AgentApprovalCard), new PropertyMetadata(true));
    /// <summary>独立审批默认展示完整代码。</summary>
    public bool ShowCode { get => (bool)GetValue(ShowCodeProperty); set => SetValue(ShowCodeProperty, value); }
    /// <summary>业务授权只由绑定命令处理，控件不连接数据库或 SSH。</summary>
    public AgentApprovalCard() => InitializeComponent();
}
