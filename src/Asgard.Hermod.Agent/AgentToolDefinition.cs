using System.Text.Json;

namespace Asgard.Hermod.Agent;

/// <summary>应用声明的工具协议，不携带连接、凭据或执行委托。</summary>
public sealed record AgentToolDefinition(string Name, string Description, JsonElement Parameters);
