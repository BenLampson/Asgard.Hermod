namespace Asgard.Hermod.UI.Controls;

/// <summary>共享消息气泡；用户、助手与工具使用稳定来源标记，业务动作由模块插槽承载。</summary>
public partial class AgentMessageView : UserControl
{
    /// <summary>单次操作的内联确认内容。</summary>
    public static readonly DependencyProperty ApprovalProperty = DependencyProperty.Register(nameof(Approval), typeof(AgentApprovalRequest), typeof(AgentMessageView));
    /// <summary>确认卡在消息历史中保留已结算状态。</summary>
    public AgentApprovalRequest? Approval { get => (AgentApprovalRequest?)GetValue(ApprovalProperty); set => SetValue(ApprovalProperty, value); }
    /// <summary>显示消息冻结的创建时间。</summary>
    public static readonly DependencyProperty TimestampLabelProperty = DependencyProperty.Register(nameof(TimestampLabel), typeof(string), typeof(AgentMessageView), new PropertyMetadata(""));
    /// <summary>角色与时间的完整本地化模板。</summary>
    public string TimestampLabel { get => (string)GetValue(TimestampLabelProperty); set => SetValue(TimestampLabelProperty, value); }
    /// <summary>共享消息的 IsExpanded 展示状态。</summary>
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(AgentMessageView), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null, (sender, value) => ((AgentMessageView)sender).IsAwaitingApproval || (bool)value));
    /// <summary>共享消息的 IsExpanded 状态。</summary>
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    /// <summary>共享消息的 IsAwaitingApproval 展示状态。</summary>
    public static readonly DependencyProperty IsAwaitingApprovalProperty = DependencyProperty.Register(nameof(IsAwaitingApproval), typeof(bool), typeof(AgentMessageView), new PropertyMetadata(false, (sender, _) => sender.CoerceValue(IsExpandedProperty)));
    /// <summary>共享消息的 IsAwaitingApproval 状态。</summary>
    public bool IsAwaitingApproval { get => (bool)GetValue(IsAwaitingApprovalProperty); set => SetValue(IsAwaitingApprovalProperty, value); }
    /// <summary>共享消息的 IsError 展示状态。</summary>
    public static readonly DependencyProperty IsErrorProperty = DependencyProperty.Register(nameof(IsError), typeof(bool), typeof(AgentMessageView), new PropertyMetadata(false));
    /// <summary>共享消息的 IsError 状态。</summary>
    public bool IsError { get => (bool)GetValue(IsErrorProperty); set => SetValue(IsErrorProperty, value); }
    /// <summary>共享消息的 ModelIdentity 展示状态。</summary>
    public static readonly DependencyProperty ModelIdentityProperty = DependencyProperty.Register(nameof(ModelIdentity), typeof(string), typeof(AgentMessageView), new PropertyMetadata(""));
    /// <summary>共享消息的 ModelIdentity 状态。</summary>
    public string ModelIdentity { get => (string)GetValue(ModelIdentityProperty); set => SetValue(ModelIdentityProperty, value); }
    /// <summary>共享消息的 Elapsed 展示状态。</summary>
    public static readonly DependencyProperty ElapsedProperty = DependencyProperty.Register(nameof(Elapsed), typeof(string), typeof(AgentMessageView), new PropertyMetadata(""));
    /// <summary>共享消息的 Elapsed 状态。</summary>
    public string Elapsed { get => (string)GetValue(ElapsedProperty); set => SetValue(ElapsedProperty, value); }
    /// <summary>共享消息的 RecoveryPrompt 展示状态。</summary>
    public static readonly DependencyProperty RecoveryPromptProperty = DependencyProperty.Register(nameof(RecoveryPrompt), typeof(string), typeof(AgentMessageView), new PropertyMetadata(""));
    /// <summary>共享消息的 RecoveryPrompt 状态。</summary>
    public string RecoveryPrompt { get => (string)GetValue(RecoveryPromptProperty); set => SetValue(RecoveryPromptProperty, value); }
    /// <summary>共享消息的 RestoreDraftCommand 展示状态。</summary>
    public static readonly DependencyProperty RestoreDraftCommandProperty = DependencyProperty.Register(nameof(RestoreDraftCommand), typeof(ICommand), typeof(AgentMessageView), new PropertyMetadata(null));
    /// <summary>共享消息的 RestoreDraftCommand 状态。</summary>
    public ICommand? RestoreDraftCommand { get => (ICommand?)GetValue(RestoreDraftCommandProperty); set => SetValue(RestoreDraftCommandProperty, value); }

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
