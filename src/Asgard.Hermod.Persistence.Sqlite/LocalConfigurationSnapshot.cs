namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>在同一读取事务中获得实例和标签，保证路径与关联来自同一个数据库快照。</summary>
public sealed record LocalConfigurationSnapshot(IReadOnlyList<StoredInstanceConfiguration> Instances,
    IReadOnlyList<ManagedTag> Tags)
{
    /// <summary>与实例和标签在同一事务读取的模型密文，导航不触发解密。</summary>
    public string? ProtectedAgentModels { get; init; }
    /// <summary>不让记录自动输出内部实例内容。</summary>
    public override string ToString() => $"Local configuration ({Instances.Count} instances, {Tags.Count} tags)";
}
