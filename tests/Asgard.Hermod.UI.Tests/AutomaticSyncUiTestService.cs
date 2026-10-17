namespace Asgard.Hermod.UI.Tests;

/// <summary>真实窗口测试的自动同步边界，网络与加密由独立集成用例覆盖。</summary>
internal sealed class AutomaticSyncUiTestService : IConfigurationSyncService
{
    public ConfigurationSyncStatus Status { get; private set; } = ConfigurationSyncStatus.PasswordRequired;
    public string Message => Asgard.Hermod.Abstractions.Localization.Text.Get("SyncStatus" + Status);
    public DateTime? LastSyncedAt { get; private set; }
    public bool HasPassword { get; private set; }
    public ConfigurationSyncStatus GetInstanceStatus(string instanceId) => Status;
    public IReadOnlyList<ConfigurationConflict> Conflicts { get; set; } = [];
    public event EventHandler? Changed;
    public int Checks { get; private set; }
    /// <summary>模拟后台同步状态变化，验证已展开菜单的实时绑定。</summary>
    internal void SetStatus(ConfigurationSyncStatus status) { Status = status; Changed?.Invoke(this, EventArgs.Empty); }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PrepareWorkspaceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnableAsync(string password, CancellationToken cancellationToken = default) { HasPassword = true; Status = ConfigurationSyncStatus.Pending; Changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    public Task ForgetPasswordAsync(CancellationToken cancellationToken = default) { HasPassword = false; Status = ConfigurationSyncStatus.PasswordRequired; Changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    public Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        Checks++; Status = Conflicts.Count > 0 ? ConfigurationSyncStatus.Conflict : ConfigurationSyncStatus.Synced;
        if (Status == ConfigurationSyncStatus.Synced) LastSyncedAt = DateTime.UnixEpoch;
        Changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask;
    }
    public Task ResolveAsync(IReadOnlyDictionary<string, bool> useCloud, CancellationToken cancellationToken = default) { Conflicts = []; return SynchronizeAsync(cancellationToken); }
    public async Task RunLocalChangeAsync(Func<Task> action, CancellationToken cancellationToken = default) { await action(); RequestSync(); }
    public void RequestSync() { Status = ConfigurationSyncStatus.Pending; Changed?.Invoke(this, EventArgs.Empty); }
}
