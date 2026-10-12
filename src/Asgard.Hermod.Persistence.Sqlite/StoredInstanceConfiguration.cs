namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>数据库内部实例记录；连接参数在到达仓储前已加密。</summary>
public sealed record StoredInstanceConfiguration(string Id, string Name, string TypeId, string ProviderId,
    byte[] ProtectedParameters, DateTime CreatedAt, IReadOnlyList<string> TagIds)
{
    /// <summary>诊断文本也不展示密文、名称或第三方配置。</summary>
    public override string ToString() => $"Stored instance ({Id}, connection hidden)";
}
