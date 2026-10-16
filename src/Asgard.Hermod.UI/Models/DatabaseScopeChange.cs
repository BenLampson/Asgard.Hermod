namespace Asgard.Hermod.UI.Models;

/// <summary>范围编辑的不可变前后快照；确认期间不得修改原范围。</summary>
public sealed record DatabaseScopeChange(string InstanceId, string SessionName, long Revision,
    string ExecutionTarget, IReadOnlyList<DatabaseIdentity> Before, IReadOnlyList<DatabaseIdentity> Selected);
