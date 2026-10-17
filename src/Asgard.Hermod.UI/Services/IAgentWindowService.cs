namespace Asgard.Hermod.UI.Services;

/// <summary>显式用户入口打开同一来源的非模态助手。</summary>
public interface IAgentWindowService
{
    /// <summary>来源已存在时恢复同一窗口，不重建对话或替换绑定。</summary>
    void Open(AgentWindowRequest request);
}
