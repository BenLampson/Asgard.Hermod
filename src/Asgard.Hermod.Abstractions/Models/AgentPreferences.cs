namespace Asgard.Hermod.Abstractions.Models;

/// <summary>仅在本机保存的模型地址与名称；此契约刻意不包含密钥或对话内容。</summary>
public sealed record AgentPreferences(string Endpoint = "", string Model = "");
