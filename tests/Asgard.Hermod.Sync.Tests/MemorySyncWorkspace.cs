namespace Asgard.Hermod.Sync.Tests;

/// <summary>支持比较后应用与账户切换的同步测试本机边界。</summary>
internal sealed class MemorySyncWorkspace(MemoryConfigurationSyncStateStore states) : ILocalWorkspaceStore
{
    public WorkspaceSnapshot Value { get; set; } = new(1, [], []);
    public bool RejectApply { get; set; }
    /// <summary>模拟提交后显示状态读取失败，不能撤销此前成功的本机保存。</summary>
    public bool RejectExport { get; set; }
    public Task<WorkspaceSnapshot> ExportAsync(CancellationToken cancellationToken = default) => RejectExport
        ? Task.FromException<WorkspaceSnapshot>(new IOException("Test export failure")) : Task.FromResult(Value);
    public Task RestoreAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default) { Value = snapshot; return Task.CompletedTask; }
    public Task RestoreIfUnchangedAsync(WorkspaceSnapshot snapshot, WorkspaceSnapshot expected, CancellationToken cancellationToken = default)
    {
        if (RejectApply || !WorkspaceContent.Equal(Value, expected)) throw Asgard.Hermod.Abstractions.Localization.Text.Exception("SyncLocalChanged");
        Value = snapshot; return Task.CompletedTask;
    }
    public async Task SwitchOwnerAsync(string owner, WorkspaceSnapshot snapshot, WorkspaceSnapshot expected, CancellationToken cancellationToken = default)
    {
        await RestoreIfUnchangedAsync(snapshot, expected, cancellationToken); states.ActiveOwner = owner;
    }
}
