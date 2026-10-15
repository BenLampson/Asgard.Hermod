namespace Asgard.Hermod.Agent;

/// <summary>模型提出的函数调用；原始 JSON 参数必须经业务白名单校验后才可使用。</summary>
public sealed record AgentToolCall(string Id, string Name, string Arguments);
