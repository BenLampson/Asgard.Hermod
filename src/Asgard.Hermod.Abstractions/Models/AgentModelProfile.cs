namespace Asgard.Hermod.Abstractions.Models;

/// <summary>模型配置的内存明文；只允许交给设备保护存储或账号加密器，不作为网络响应或日志。</summary>
public sealed record AgentModelProfile(string Id, string Name, string Endpoint, string Model, string ApiKey, DateTimeOffset UpdatedAt)
{
    /// <summary>调试与字符串插值不暴露配置或 API Key。</summary>
    public override string ToString() => nameof(AgentModelProfile);
}
