namespace Asgard.Hermod.Agent;

/// <summary>具有模型目录的传输可为每个来源建立独立选择，测试客户端无需实现。</summary>
public interface IAgentSessionClientFactory
{
    /// <summary>未装配模型目录时返回空，保持独立传输模块兼容。</summary>
    AgentModelSession? CreateSession();
}
