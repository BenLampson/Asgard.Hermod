namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>单条可复制的助手消息与独立的 SQL 建议，不把模型输出作为应用指令。</summary>
public sealed class PostgreSqlAssistantMessage(string content, bool fromUser, IReadOnlyList<PostgreSqlAssistantSql>? sqlBlocks = null)
{
    /// <summary>模型和用户原文不进行资源翻译。</summary>
    public string Content { get; } = content;
    /// <summary>消息来源使用本地化标题。</summary>
    public string Role => Text.Get(fromUser ? "PostgreSqlAssistantYou" : "PostgreSqlAssistantTitle");
    /// <summary>模型返回的完整 SQL 围栏。</summary>
    public IReadOnlyList<PostgreSqlAssistantSql> SqlBlocks { get; } = sqlBlocks ?? [];
}
