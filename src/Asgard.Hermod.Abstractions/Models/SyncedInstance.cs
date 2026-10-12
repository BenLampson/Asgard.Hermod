namespace Asgard.Hermod.Abstractions.Models;

/// <summary>可移植实例配置，凭据包含在参数中，禁止日志输出。</summary>
public sealed record SyncedInstance(string Id, string Name, string TypeId, string ProviderId,
    IReadOnlyDictionary<string, string> Parameters, DateTime CreatedAt, IReadOnlyList<string> TagIds)
{
    /// <summary>避免调试输出泄露连接参数。</summary>
    public override string ToString() => nameof(SyncedInstance);
}
