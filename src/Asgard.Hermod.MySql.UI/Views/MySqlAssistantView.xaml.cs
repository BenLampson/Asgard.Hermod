namespace Asgard.Hermod.MySql.UI.Views;

/// <summary>数据库模块只提供上下文和 SQL 动作，共享面板负责对话展示与输入交互。</summary>
public partial class MySqlAssistantView : UserControl
{
    /// <summary>加载共享聊天面板，保留原有页签、会话和业务命令绑定。</summary>
    public MySqlAssistantView() => InitializeComponent();
}
