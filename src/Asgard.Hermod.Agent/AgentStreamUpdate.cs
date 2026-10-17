namespace Asgard.Hermod.Agent;

/// <summary>一次模型响应的累计快照；思考与正文独立，未完成的工具参数不向界面暴露。</summary>
public sealed record AgentStreamUpdate(string Content, string Thinking, bool IsThinking, TimeSpan ThinkingElapsed);
