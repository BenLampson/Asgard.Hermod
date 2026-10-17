namespace Asgard.Hermod.UI.Models;

/// <summary>聊天历史只依赖原始正文和稳定来源标记，不解析本地化角色名称。</summary>
public interface IAgentDisplayMessage
{
    /// <summary>用户或模型的原始正文。</summary>
    string Content { get; }
    /// <summary>是否来自用户。</summary>
    bool IsUser { get; }
}
