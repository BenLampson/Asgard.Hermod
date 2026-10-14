namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>隔离 SQLite 连接、密文记录和事务；界面通过应用服务消费安全快照。</summary>
public interface ILocalConfigurationRepository
{
    /// <summary>读取实例与完整标签快照，不进行任何凭据解密。</summary>
    LocalConfigurationSnapshot GetSnapshot(CancellationToken cancellationToken = default);
    /// <summary>事务只更新指定元数据列和关联，提交前准备返回快照，不触碰密文。</summary>
    LocalConfigurationSnapshot UpdateInstanceMetadata(string instanceId, InstanceMetadataChange change,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(Text.Get("TheCurrentConfigurationServiceDoesNotSupportEditingInstances"));
    /// <summary>以当前事务中的父级和颜色验证名称，只写名称及规范化名称。</summary>
    ManagedTag RenameTag(string tagId, string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(Text.Get("TheOperationFailedTryAgain"));
    /// <summary>读取指定实例的密文记录，不存在时返回 null。</summary>
    StoredInstanceConfiguration? GetInstance(string instanceId, CancellationToken cancellationToken = default);
    /// <summary>在同一事务中保存实例与已验证存在的标签关联。</summary>
    void CreateInstance(StoredInstanceConfiguration instance, CancellationToken cancellationToken = default);
    /// <summary>只原子更新名称、类型和密文参数，保留原实例标识、创建时间与全部标签关联。</summary>
    void UpdateInstance(StoredInstanceConfiguration instance, CancellationToken cancellationToken = default);
    /// <summary>显式替换标签时配置与关联同事务提交；未替换时沿用原连接更新语义。</summary>
    void UpdateInstance(StoredInstanceConfiguration instance, bool replaceTags, CancellationToken cancellationToken = default)
    {
        if (replaceTags) throw new NotSupportedException(Text.Get("TheCurrentConfigurationServiceDoesNotSupportEditingInstances"));
        UpdateInstance(instance, cancellationToken);
    }
    /// <summary>在写事务中执行标签验证和新增或更新，返回稳定标识的快照。</summary>
    ManagedTag SaveTag(string? tagId, TagEditDraft draft, CancellationToken cancellationToken = default);
    /// <summary>读取删除子树及去重后的受影响实例数。</summary>
    TagDeletionPreview PreviewTagDeletion(string tagId, CancellationToken cancellationToken = default);
    /// <summary>原子删除标签子树及关联，保留所有实例。</summary>
    void DeleteTag(string tagId, CancellationToken cancellationToken = default);
}
