namespace Asgard.Hermod.UI.Controls;

public partial class AgentMessageView
{
    /// <summary>思考状态独立于工具步骤的折叠状态。</summary>
    public static readonly DependencyProperty ThinkingProperty = DependencyProperty.Register(nameof(Thinking), typeof(AgentThinkingState), typeof(AgentMessageView));
    /// <summary>当前消息的真实思考状态。</summary>
    public AgentThinkingState? Thinking { get => (AgentThinkingState?)GetValue(ThinkingProperty); set => SetValue(ThinkingProperty, value); }
    /// <summary>回复完成前隐藏完整回复动作。</summary>
    public static readonly DependencyProperty IsStreamingProperty = DependencyProperty.Register(nameof(IsStreaming), typeof(bool), typeof(AgentMessageView));
    /// <summary>当前响应是否仍在接收。</summary>
    public bool IsStreaming { get => (bool)GetValue(IsStreamingProperty); set => SetValue(IsStreamingProperty, value); }
}
