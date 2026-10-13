namespace Asgard.Hermod.Sync.Tests;

/// <summary>支持比较后应用与账户切换的同步测试本机边界。</summary>
internal sealed class MemorySyncWorkspace(MemoryConfigurationSyncStateStore states) : ILocalWorkspaceStore
{
    public WorkspaceSnapshot Value { get; set; } = new(1, [], []);
    public bool RejectApply { get; set; }
    public Task<WorkspaceSnapshot> ExportAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
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
