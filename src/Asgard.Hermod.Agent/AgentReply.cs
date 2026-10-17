namespace Asgard.Hermod.Agent;

/// <summary>模型原文、SQL 建议和显式工具调用；处理方式与执行权限由调用方确定。</summary>
public sealed record AgentReply(string Content, IReadOnlyList<string> SqlBlocks)
{
    /// <summary>供应方显式返回的思考文本，不进入正文、执行建议和后续对话历史。</summary>
    public string Thinking { get; init; } = "";
    /// <summary>从首个思考片段至正文或完成事件的实际耗时。</summary>
    public TimeSpan ThinkingElapsed { get; init; }
    /// <summary>本次生成的模型协议标识，必须在请求完成时冻结。</summary>
    public string ModelIdentity { get; init; } = "";
    /// <summary>仅供显式启用工具的排障或 SQL 查询会话处理，普通文本会话不接受此字段。</summary>
    public IReadOnlyList<AgentToolCall> ToolCalls { get; init; } = [];
    /// <summary>从完整回复中提取显式 SQL 代码块，不把一般解释识别为 SQL。</summary>
    public static AgentReply FromContent(string content) => new(content, AgentSqlBlocks.Extract(content));
}
