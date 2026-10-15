namespace Asgard.Hermod.UI.Models;

/// <summary>确认弹窗使用不可变范围快照，禁止迟到确认作用于已改变的 Session。</summary>
public sealed record DatabaseRemovalRequest(string InstanceId, string SessionName, string Database,
    string ExecutionTarget, IReadOnlyList<string> RemainingDatabases, long Revision);
