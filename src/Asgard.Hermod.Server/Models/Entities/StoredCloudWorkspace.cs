namespace Asgard.Hermod.Server;

/// <summary>服务器只保存密文和归属审计，revision 用于事务中的比较交换。</summary>
public sealed record StoredCloudWorkspace(string OwnerKey, string TenantId, string ApplicationId, string SubjectId,
    string Revision, string ProtectedJson, DateTime CreatedAt, DateTime UpdatedAt);
