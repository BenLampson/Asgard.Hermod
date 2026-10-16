namespace Asgard.Hermod.Agent;

/// <summary>随冻结日志发送的来源与可见范围，只作为证据元数据，不产生工具权限。</summary>
public sealed record LogEvidenceRange(string DocumentId, string Host, string ContainerId, int RequestedLines,
    int StartCharacter, int EndCharacter, int FromLine, int ToLine, bool IsSelection);
