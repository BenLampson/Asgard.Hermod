namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>单条可复制的助手消息与独立的 SQL 建议，不把模型输出作为应用指令。</summary>
public sealed class PostgreSqlAssistantMessage(string content, bool fromUser, IReadOnlyList<PostgreSqlAssistantSql>? sqlBlocks = null, bool isTool = false) : ObservableObject
{
    /// <summary>模型和用户原文不进行资源翻译。</summary>
    public string Content { get; private set => SetProperty(ref field, value); } = content;
    /// <summary>通过消息来源决定是否渲染 Markdown，不依赖本地化角色标签。</summary>
    public bool IsAssistant => !fromUser && !isTool;
    /// <summary>共享消息气泡使用稳定来源标记，不比较角色显示名称。</summary>
    public bool IsUser => fromUser;
    /// <summary>消息来源使用本地化标题。</summary>
    public string Role => Text.Get(isTool ? "SqlAgentQueryStep" : fromUser ? "PostgreSqlAssistantYou" : "PostgreSqlAssistantTitle");
    /// <summary>自动查询记录保持字面内容，不提取为可再次执行的 SQL 建议。</summary>
    public bool IsTool => isTool;
    /// <summary>当前查询的本地化状态。</summary>
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>更新同一条查询记录，避免运行和完成状态重复显示。</summary>
    internal void Update(string body, string statusKey) { Content = body; Status = Text.Get(statusKey); }
    /// <summary>模型返回的完整 SQL 围栏。</summary>
    public IReadOnlyList<PostgreSqlAssistantSql> SqlBlocks { get; } = sqlBlocks ?? [];
}
