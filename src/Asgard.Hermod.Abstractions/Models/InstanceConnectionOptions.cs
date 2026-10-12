namespace Asgard.Hermod.Abstractions.Models;

/// <summary>实例类型和具体连接器共同标识配置，允许各类型定义专属连接字段。</summary>
public sealed class InstanceConnectionOptions
{
    /// <summary>顶层实例类型，例如 storage 或 docker。</summary>
    public required string TypeId { get; init; }
    /// <summary>已注册的服务商标识。</summary>
    public required string ProviderId { get; init; }
    /// <summary>服务商配置参数；可能包含凭据，不得写入日志或导航快照。</summary>
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }

    /// <summary>避免诊断输出意外泄漏参数。</summary>
    public override string ToString() => $"Instance connection ({TypeId}/{ProviderId}, parameters hidden)";
}
