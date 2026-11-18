namespace Asgard.Hermod.Mcp;

/// <summary>当前连接内获确认的实例及其连接版本，不写入本机策略。</summary>
internal sealed record McpApprovalScope(string InstanceId, string TypeId, string Revision);
