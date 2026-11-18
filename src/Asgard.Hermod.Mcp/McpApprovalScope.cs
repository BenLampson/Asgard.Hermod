namespace Asgard.Hermod.Mcp;

/// <summary>用于匹配设置授权和本次连接确认的实例身份与版本。</summary>
internal sealed record McpApprovalScope(string InstanceId, string TypeId, string Revision);
