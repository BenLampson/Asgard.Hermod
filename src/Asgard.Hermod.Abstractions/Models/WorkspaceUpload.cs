namespace Asgard.Hermod.Abstractions.Models;

/// <summary>上传必须携带设备已知版本，不接受客户端指定所有者。</summary>
public sealed record WorkspaceUpload(string ExpectedRevision, EncryptedWorkspace Workspace);
