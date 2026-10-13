namespace Asgard.Hermod.Server;

/// <summary>隔离服务器数据库和原子版本检查。</summary>
public interface ICloudWorkspaceRepository
{
    /// <summary>初始化专用数据库基线，拒绝未知未来版本。</summary>
    void Initialize();
    /// <summary>只读取当前所有者的快照。</summary>
    StoredCloudWorkspace? Read(string ownerKey, CancellationToken cancellationToken);
    /// <summary>在写事务中检查已知版本并保存，冲突返回空值。</summary>
    StoredCloudWorkspace? Save(string ownerKey, HermodAccount account, string expectedRevision, string encryptedJson, CancellationToken cancellationToken);
    /// <summary>读取逐条配置或等待迁移的旧密文。</summary>
    CloudConfiguration ReadConfiguration(string ownerKey, CancellationToken cancellationToken);
    /// <summary>事务比较集合和条目版本，归属与审计由服务器产生。</summary>
    CloudConfiguration? SaveConfiguration(string ownerKey, HermodAccount account, ConfigurationUpload upload, CancellationToken cancellationToken);
}
