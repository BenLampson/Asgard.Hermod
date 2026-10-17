namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>单条可复制的助手消息与独立的 SQL 建议，不把模型输出作为应用指令。</summary>
public sealed class MySqlAssistantMessage(string content, bool fromUser, IReadOnlyList<MySqlAssistantSql>? sqlBlocks = null, bool isTool = false, bool isError = false, string recoveryPrompt = "", string modelIdentity = "", AgentApprovalRequest? approval = null) : AgentDisplayMessage(content, fromUser, isTool, isError, recoveryPrompt, modelIdentity, approval)
{
    /// <inheritdoc />
    public override void CompleteStream(AgentDisplayMessage final, Asgard.Hermod.Agent.AgentReply reply)
    {
        base.CompleteStream(final, reply); SqlBlocks = ((MySqlAssistantMessage)final).SqlBlocks;
    }
    /// <summary>消息来源使用本地化标题。</summary>
    public override string Role => Text.Get(IsTool ? "SqlAgentQueryStep" : IsUser ? "MySqlAssistantYou" : "MySqlAssistantTitle");
    /// <summary>更新同一条查询记录，避免运行和完成状态重复显示。</summary>
    internal void Update(string body, string statusKey) => UpdateMessage(body, statusKey);
    /// <summary>模型返回的完整 SQL 围栏。</summary>
    public IReadOnlyList<MySqlAssistantSql> SqlBlocks { get; private set => SetProperty(ref field, value); } = sqlBlocks ?? [];
}
