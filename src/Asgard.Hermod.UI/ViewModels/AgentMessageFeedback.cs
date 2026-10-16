namespace Asgard.Hermod.UI.ViewModels;

/// <summary>仅属于当前内存对话的回复反馈，不上传或触发模型请求。</summary>
public enum AgentMessageFeedback
{
    /// <summary>未标记或已撤销。</summary>
    None,
    /// <summary>对当前任务有帮助。</summary>
    Helpful,
    /// <summary>对当前任务没有帮助。</summary>
    Unhelpful,
}
