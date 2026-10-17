using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.ViewModels;

public partial class AgentDisplayMessage
{
    /// <summary>思考和正文拥有独立的内容与展开状态。</summary>
    public AgentThinkingState Thinking { get; } = new();
    /// <summary>响应未结束时不展示完成动作或提取可执行建议。</summary>
    public bool IsStreaming { get; private set => SetProperty(ref field, value); }
    internal void ApplyStream(AgentStreamUpdate update)
    {
        IsStreaming = true; Content = update.Content; OriginalContent = update.Content; Thinking.Apply(update); OnPropertyChanged(nameof(Thinking));
    }
    /// <summary>在同一消息对象上安装已验证的回复，保留思考展开状态和选中对象。</summary>
    public virtual void CompleteStream(AgentDisplayMessage final, AgentReply reply)
    {
        Content = final.Content; OriginalContent = final.OriginalContent; ModelIdentity = final.ModelIdentity;
        Thinking.Apply(new(reply.Content, reply.Thinking, false, reply.ThinkingElapsed)); IsStreaming = false;
    }
    internal void InterruptStream() { Thinking.Interrupt(); IsStreaming = false; }
}
