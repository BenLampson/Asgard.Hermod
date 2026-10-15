namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>单条可复制的助手消息与独立的 SQL 建议，不把模型输出作为应用指令。</summary>
public sealed class MySqlAssistantMessage(string content, bool fromUser, IReadOnlyList<MySqlAssistantSql>? sqlBlocks = null)
{
    /// <summary>模型和用户原文不进行资源翻译。</summary>
    public string Content { get; } = content;
    /// <summary>消息来源使用本地化标题。</summary>
    public string Role => Text.Get(fromUser ? "MySqlAssistantYou" : "MySqlAssistantTitle");
    /// <summary>模型返回的完整 SQL 围栏。</summary>
    public IReadOnlyList<MySqlAssistantSql> SqlBlocks { get; } = sqlBlocks ?? [];
}
