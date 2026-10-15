namespace Asgard.Hermod.Agent;

/// <summary>模型原文与可由用户明确应用到编辑器的 SQL 建议；本模块不执行建议。</summary>
public sealed record AgentReply(string Content, IReadOnlyList<string> SqlBlocks)
{
    /// <summary>从完整回复中提取显式 SQL 代码块，不把一般解释识别为 SQL。</summary>
    public static AgentReply FromContent(string content) => new(content, AgentSqlBlocks.Extract(content));
}
