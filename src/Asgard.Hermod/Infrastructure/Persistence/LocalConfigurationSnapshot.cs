namespace Asgard.Hermod.Infrastructure.Persistence;

/// <summary>在同一读取事务中获得实例和标签，保证路径与关联来自同一个数据库快照。</summary>
public sealed record LocalConfigurationSnapshot(IReadOnlyList<StoredInstanceConfiguration> Instances,
    IReadOnlyList<ManagedTag> Tags)
{
    /// <summary>不让记录自动输出内部实例内容。</summary>
    public override string ToString() => $"Local configuration ({Instances.Count} instances, {Tags.Count} tags)";
}
