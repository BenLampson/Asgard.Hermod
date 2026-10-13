namespace Asgard.Hermod.Sync.Tests;

/// <summary>自动同步通过真实 HTTP API 验证跨设备合并、冲突和本机账户隔离。</summary>
public sealed class ConfigurationSyncServiceTests
{
    /// <summary>同步标记刷新失败只能保守隐藏已同步状态，不能把成功保存误报成失败。</summary>
    [Fact]
    public async Task InstanceStatus_ExportFailureDoesNotFailCommittedLocalChange()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync();
        var states = new MemoryConfigurationSyncStateStore();
        var local = new MemorySyncWorkspace(states) { Value = Initial };
        using var sync = new ConfigurationSyncService(Account(host), local, states, new SyncTestClientFactory(host), Options(host), new ConfigurationTestRegistry());
        await sync.EnableAsync(Password, TestContext.Current.CancellationToken);
        await sync.SynchronizeAsync(TestContext.Current.CancellationToken);
        await sync.RunLocalChangeAsync(() =>
        {
            local.Value = Initial with { Instances = [Initial.Instances[0] with { Name = "Committed change" }] };
            local.RejectExport = true;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
        Assert.Equal("Committed change", local.Value.Instances[0].Name);
        Assert.Equal(ConfigurationSyncStatus.Pending, sync.GetInstanceStatus("one"));
        local.RejectExport = false;
        await sync.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConfigurationSyncStatus.Synced, sync.GetInstanceStatus("one"));
    }

    /// <summary>实例独立比较配置及关联标签；其他实例待上传不能清除已同步标志。</summary>
    [Fact]
    public async Task InstanceStatus_FollowsOwnContentAndHidesAfterSignOut()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync();
        var states = new MemoryConfigurationSyncStateStore();
        var second = Initial.Instances[0] with { Id = "two", Name = "Second" };
        var root = new SyncedTag("root", "Root", null, "#112233", false);
        var child = new SyncedTag("child", "Child", "root", "#112233", false);
        var local = new MemorySyncWorkspace(states)
        {
            Value = Initial with
            {
                Instances = [Initial.Instances[0] with { TagIds = ["child"] }, second],
                Tags = [root, child],
            }
        };
        var account = Account(host);
        using var sync = new ConfigurationSyncService(account, local, states, new SyncTestClientFactory(host), Options(host), new ConfigurationTestRegistry());
        await sync.PrepareWorkspaceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConfigurationSyncStatus.PasswordRequired, sync.GetInstanceStatus("one"));
        await sync.EnableAsync(Password, TestContext.Current.CancellationToken);
        await sync.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConfigurationSyncStatus.Synced, sync.GetInstanceStatus("one"));
        Assert.Equal(ConfigurationSyncStatus.Synced, sync.GetInstanceStatus("two"));
        await sync.RunLocalChangeAsync(() =>
        {
            local.Value = local.Value with { Tags = [root with { Name = "Renamed root" }, child] };
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
        Assert.Equal(ConfigurationSyncStatus.Pending, sync.GetInstanceStatus("one"));
        Assert.Equal(ConfigurationSyncStatus.Synced, sync.GetInstanceStatus("two"));
        await sync.SynchronizeAsync(TestContext.Current.CancellationToken);
        await sync.RunLocalChangeAsync(() =>
        {
            local.Value = local.Value with { Instances = [local.Value.Instances[0], second with { Name = "Edited second" }, second with { Id = "three" }] };
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
        Assert.Equal(ConfigurationSyncStatus.Synced, sync.GetInstanceStatus("one"));
        Assert.Equal(ConfigurationSyncStatus.Pending, sync.GetInstanceStatus("two"));
        Assert.Equal(ConfigurationSyncStatus.Pending, sync.GetInstanceStatus("three"));
        await sync.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConfigurationSyncStatus.Synced, sync.GetInstanceStatus("three"));
        account.Change("user-b");
        Assert.NotEqual(ConfigurationSyncStatus.Synced, sync.GetInstanceStatus("one"));
        account.Change(null);
        Assert.Equal(ConfigurationSyncStatus.Local, sync.GetInstanceStatus("one"));
    }

    /// <summary>后台服务在密码设置和本地更改后自行上传，不需要手动同步命令。</summary>
    [Fact]
    public async Task BackgroundSync_UploadsAfterUnlockAndLocalChanges()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync(); var states = new MemoryConfigurationSyncStateStore();
        var local = new MemorySyncWorkspace(states) { Value = Initial };
        using var sync = new ConfigurationSyncService(Account(host), local, states, new SyncTestClientFactory(host), Options(host), new ConfigurationTestRegistry());
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sync.Changed += (_, _) => { if (sync.Status == ConfigurationSyncStatus.Synced) completed.TrySetResult(); };
        try
        {
            await sync.StartAsync(TestContext.Current.CancellationToken); await sync.EnableAsync(Password, TestContext.Current.CancellationToken);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await sync.RunLocalChangeAsync(() =>
            {
                local.Value = Initial with { Instances = [Initial.Instances[0] with { Name = "Background update" }] }; return Task.CompletedTask;
            }, TestContext.Current.CancellationToken);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            host.SignIn(host.Token()); var json = await host.Client.GetFromJsonAsync<JsonElement>("/api/sync/configuration", TestContext.Current.CancellationToken);
            Assert.Equal("Background update", json.GetProperty("data").GetProperty("instances")[0].GetProperty("name").GetString());
        }
        finally { await sync.StopAsync(TestContext.Current.CancellationToken); }
    }
    private const string Password = "automatic-sync-password";
    private static WorkspaceSnapshot Initial => new(1, [new("one", "Original", "storage", "provider",
        new Dictionary<string, string> { ["endpoint"] = "original", ["secret"] = "private-original" }, DateTime.UnixEpoch, [])], []);
    private static HermodIdentityOptions Options(ApiTestHost host) => new() { Authority = host.Options.Authority, ApplicationId = host.Options.ApplicationId };
    private static SyncTestAccount Account(ApiTestHost host) => new(host.Options.TenantId, host.Options.ApplicationId) { Token = host.Token() };
    /// <summary>首次设置后无需再次输入密码，独立字段变化在两台设备同步。</summary>
    [Fact]
    public async Task TwoDevices_AutomaticallyMergeIndependentEdits()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync();
        var firstStates = new MemoryConfigurationSyncStateStore(); var firstLocal = new MemorySyncWorkspace(firstStates) { Value = Initial };
        using var first = new ConfigurationSyncService(Account(host), firstLocal, firstStates, new SyncTestClientFactory(host), Options(host), new ConfigurationTestRegistry());
        await first.EnableAsync(Password, TestContext.Current.CancellationToken); await first.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.True(first.HasPassword); Assert.Equal(ConfigurationSyncStatus.Synced, first.Status);
        var secondStates = new MemoryConfigurationSyncStateStore(); var secondLocal = new MemorySyncWorkspace(secondStates);
        using var second = new ConfigurationSyncService(Account(host), secondLocal, secondStates, new SyncTestClientFactory(host), Options(host), new ConfigurationTestRegistry());
        await second.EnableAsync(Password, TestContext.Current.CancellationToken); await second.SynchronizeAsync(TestContext.Current.CancellationToken);
        firstLocal.Value = Initial with { Instances = [Initial.Instances[0] with { Name = "First device" }] };
        secondLocal.Value = Initial with { Instances = [Initial.Instances[0] with { Parameters = new Dictionary<string, string>(Initial.Instances[0].Parameters) { ["endpoint"] = "second-device" } }] };
        await first.SynchronizeAsync(TestContext.Current.CancellationToken); await second.SynchronizeAsync(TestContext.Current.CancellationToken); await first.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.Empty(second.Conflicts); Assert.Equal("First device", secondLocal.Value.Instances[0].Name);
        Assert.Equal("second-device", firstLocal.Value.Instances[0].Parameters["endpoint"]); Assert.True(WorkspaceContent.Equal(firstLocal.Value, secondLocal.Value));
    }
    /// <summary>同字段冲突只在明确选择后提交，错误密码不得替换已保存密码。</summary>
    [Fact]
    public async Task Conflict_PreservesBothSidesUntilResolved()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync();
        var s1 = new MemoryConfigurationSyncStateStore(); var l1 = new MemorySyncWorkspace(s1) { Value = Initial };
        var s2 = new MemoryConfigurationSyncStateStore(); var l2 = new MemorySyncWorkspace(s2);
        using var first = new ConfigurationSyncService(Account(host), l1, s1, new SyncTestClientFactory(host), Options(host), new ConfigurationTestRegistry());
        using var second = new ConfigurationSyncService(Account(host), l2, s2, new SyncTestClientFactory(host), Options(host), new ConfigurationTestRegistry());
        await first.EnableAsync(Password, TestContext.Current.CancellationToken); await first.SynchronizeAsync(TestContext.Current.CancellationToken); await second.EnableAsync(Password, TestContext.Current.CancellationToken); await second.SynchronizeAsync(TestContext.Current.CancellationToken);
        l1.Value = Initial with { Instances = [Initial.Instances[0] with { Name = "Cloud change" }] };
        l2.Value = Initial with { Instances = [Initial.Instances[0] with { Name = "Local change" }] };
        await first.SynchronizeAsync(TestContext.Current.CancellationToken); await second.SynchronizeAsync(TestContext.Current.CancellationToken); Assert.Equal(ConfigurationSyncStatus.Conflict, second.Status);
        var conflict = Assert.Single(second.Conflicts); Assert.Equal("Local change", l2.Value.Instances[0].Name);
        Assert.Equal(ConfigurationSyncStatus.Conflict, second.GetInstanceStatus("one"));
        await second.ResolveAsync(new Dictionary<string, bool> { [conflict.Key] = true }, TestContext.Current.CancellationToken); Assert.Equal("Cloud change", l2.Value.Instances[0].Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.EnableAsync("wrong-password-123", TestContext.Current.CancellationToken)); Assert.True(second.HasPassword);
        await second.SynchronizeAsync(TestContext.Current.CancellationToken); Assert.Equal(ConfigurationSyncStatus.Synced, second.Status);
    }
    /// <summary>账户 B 不获得账户 A 的配置，退出恢复登录前本地工作区。</summary>
    [Fact]
    public async Task SwitchingAccounts_KeepsSeparateProtectedWorkingCopies()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync(); var states = new MemoryConfigurationSyncStateStore();
        var local = new MemorySyncWorkspace(states) { Value = Initial }; var account = Account(host);
        using var sync = new ConfigurationSyncService(account, local, states, new SyncTestClientFactory(host), Options(host), new ConfigurationTestRegistry());
        await sync.PrepareWorkspaceAsync(TestContext.Current.CancellationToken); local.Value = Initial with { Instances = [Initial.Instances[0] with { Name = "Only account A" }] };
        account.Change("user-b"); await sync.PrepareWorkspaceAsync(TestContext.Current.CancellationToken); Assert.Empty(local.Value.Instances); Assert.False(sync.HasPassword);
        account.Change("user-a"); await sync.PrepareWorkspaceAsync(TestContext.Current.CancellationToken); Assert.Equal("Only account A", local.Value.Instances[0].Name);
        account.Change(null); await sync.PrepareWorkspaceAsync(TestContext.Current.CancellationToken); Assert.Equal("Original", local.Value.Instances[0].Name); Assert.Equal(ConfigurationSyncStatus.Local, sync.Status);
    }
    /// <summary>网络期间本机应用失败不能推进共同基准，重试仍保留云端变化。</summary>
    [Fact]
    public async Task FailedLocalApply_RetryRetainsCloudChanges()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync();
        var s1 = new MemoryConfigurationSyncStateStore(); var l1 = new MemorySyncWorkspace(s1) { Value = Initial };
        var s2 = new MemoryConfigurationSyncStateStore(); var l2 = new MemorySyncWorkspace(s2);
        using var first = new ConfigurationSyncService(Account(host), l1, s1, new SyncTestClientFactory(host), Options(host), new ConfigurationTestRegistry());
        using var second = new ConfigurationSyncService(Account(host), l2, s2, new SyncTestClientFactory(host), Options(host), new ConfigurationTestRegistry());
        await first.EnableAsync(Password, TestContext.Current.CancellationToken); await first.SynchronizeAsync(TestContext.Current.CancellationToken); await second.EnableAsync(Password, TestContext.Current.CancellationToken); await second.SynchronizeAsync(TestContext.Current.CancellationToken);
        l1.Value = Initial with { Instances = [Initial.Instances[0] with { Name = "Cloud update" }] }; await first.SynchronizeAsync(TestContext.Current.CancellationToken);
        l2.RejectApply = true; await Assert.ThrowsAsync<InvalidOperationException>(() => second.SynchronizeAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Original", l2.Value.Instances[0].Name); l2.RejectApply = false;
        Assert.Equal(ConfigurationSyncStatus.Failed, second.GetInstanceStatus("one"));
        await second.SynchronizeAsync(TestContext.Current.CancellationToken); Assert.Equal("Cloud update", l2.Value.Instances[0].Name);
    }
}
