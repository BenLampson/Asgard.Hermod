namespace Asgard.Hermod.UI.Controls;

/// <summary>共享消息气泡；用户、助手与工具使用稳定来源标记，业务动作由模块插槽承载。</summary>
public partial class AgentMessageView : UserControl
{
    /// <summary>本地化角色名称。</summary>
    public static readonly DependencyProperty RoleProperty = DependencyProperty.Register(nameof(Role), typeof(string), typeof(AgentMessageView), new PropertyMetadata(""));
    /// <summary>消息原文；用户与工具正文不解析 Markdown。</summary>
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(nameof(Body), typeof(string), typeof(AgentMessageView), new PropertyMetadata(""));
    /// <summary>用户消息使用主题强调底色。</summary>
    public static readonly DependencyProperty IsUserProperty = DependencyProperty.Register(nameof(IsUser), typeof(bool), typeof(AgentMessageView));
    /// <summary>工具消息使用独立边框与字面正文。</summary>
    public static readonly DependencyProperty IsToolProperty = DependencyProperty.Register(nameof(IsTool), typeof(bool), typeof(AgentMessageView));
    /// <summary>可选的本地化工具步骤状态。</summary>
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(string), typeof(AgentMessageView), new PropertyMetadata(""));
    /// <summary>模块提供的建议和动作，不包含共享层可执行的业务逻辑。</summary>
    public static readonly DependencyProperty ActionsContentProperty = DependencyProperty.Register(nameof(ActionsContent), typeof(object), typeof(AgentMessageView));

    /// <summary>加载统一消息排版与主题资源。</summary>
    public AgentMessageView() => InitializeComponent();
    /// <summary>角色显示文本。</summary>
    public string Role { get => (string)GetValue(RoleProperty); set => SetValue(RoleProperty, value); }
    /// <summary>消息原文。</summary>
    public string Body { get => (string)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }
    /// <summary>是否由用户发送。</summary>
    public bool IsUser { get => (bool)GetValue(IsUserProperty); set => SetValue(IsUserProperty, value); }
    /// <summary>是否为工具步骤。</summary>
    public bool IsTool { get => (bool)GetValue(IsToolProperty); set => SetValue(IsToolProperty, value); }
    /// <summary>步骤状态。</summary>
    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    /// <summary>业务建议插槽内容。</summary>
    public object? ActionsContent { get => GetValue(ActionsContentProperty); set => SetValue(ActionsContentProperty, value); }
}
