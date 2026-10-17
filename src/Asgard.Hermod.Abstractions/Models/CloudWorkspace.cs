namespace Asgard.Hermod.Abstractions.Models;

/// <summary>服务器快照及不透明并发版本；空快照使用版本零。</summary>
public sealed record CloudWorkspace(string Revision, EncryptedWorkspace? Workspace, DateTime? UpdatedAt);
