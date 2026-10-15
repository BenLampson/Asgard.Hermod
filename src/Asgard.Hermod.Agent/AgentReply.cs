namespace Asgard.Hermod.Agent;

/// <summary>模型原文、SQL 建议和显式工具调用；处理方式与执行权限由调用方确定。</summary>
public sealed record AgentReply(string Content, IReadOnlyList<string> SqlBlocks)
{
    /// <summary>仅供启用工具的 SSH 排障会话处理，普通文本会话不接受此字段。</summary>
    public IReadOnlyList<AgentToolCall> ToolCalls { get; init; } = [];
    /// <summary>从完整回复中提取显式 SQL 代码块，不把一般解释识别为 SQL。</summary>
    public static AgentReply FromContent(string content) => new(content, AgentSqlBlocks.Extract(content));
}
