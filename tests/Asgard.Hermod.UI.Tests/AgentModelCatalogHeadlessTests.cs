using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.Tests;

/// <summary>模型选择、冻结任务、账号撤销和过期编辑不依赖原生窗口。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentModelCatalogHeadlessTests(WpfTestFixture fixture)
{
    [Fact]
    public Task OwnerCommitBeforeModelNotificationCannotExposeCachedPreviousProfiles() => fixture.RunOffscreenAsync(() =>
    {
        using var runtime = new AgentModelTestRuntime(); using var previous = runtime.Client.CreateSession();
        runtime.Services.GetRequiredService<IConfigurationSyncStateStore>().ActiveOwner = "new-owner";
        Assert.False(runtime.Catalog.IsReady); Assert.Empty(runtime.Catalog.Current.Profiles); Assert.False(previous.IsConfigured);
        using var duringCommit = runtime.Client.CreateSession(); Assert.Empty(duringCommit.Profiles);
        runtime.Store.Save(new([AgentModelTestRuntime.Second], AgentModelTestRuntime.Second.Id));
        Assert.True(runtime.Catalog.IsReady); Assert.False(previous.IsOwnerCurrent); Assert.False(duringCommit.IsOwnerCurrent);
        using var current = runtime.Client.CreateSession(); Assert.Equal(AgentModelTestRuntime.Second.Id, current.SelectedId);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DefaultOnlySeedsNewSessionsAndBusySwitchWaitsForTaskExit() => fixture.RunOffscreenAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime();
        using var existing = runtime.Client.CreateSession();
        await runtime.Catalog.SetDefaultAsync(AgentModelTestRuntime.Second.Id, runtime.Catalog.Epoch);
        Assert.Equal(AgentModelTestRuntime.First.Id, existing.SelectedId);
        using var later = runtime.Client.CreateSession(); Assert.Equal(AgentModelTestRuntime.Second.Id, later.SelectedId);
        using var task = existing.BeginTask(TestContext.Current.CancellationToken);
        using (var borrowed = existing.BeginTask(task.Token)) Assert.Same(task.Client, borrowed.Client);
        Assert.False(task.Token.IsCancellationRequested);
        Assert.Throws<InvalidOperationException>(() => existing.Select(AgentModelTestRuntime.Second.Id));
        var switching = existing.StopAndSelectAsync(AgentModelTestRuntime.Second.Id, TestContext.Current.CancellationToken);
        Assert.True(task.Token.IsCancellationRequested); Assert.False(switching.IsCompleted);
        Assert.Equal(AgentModelTestRuntime.First.Endpoint, Assert.Single(runtime.Client.Captured).Options.Endpoint);
        task.Dispose(); await switching;
        Assert.Equal(AgentModelTestRuntime.Second.Id, existing.SelectedId);
    });

    [Fact]
    public Task ProfileChangeCancelsWholeTaskAndDeleteNeverFallsBackToDefault() => fixture.RunOffscreenAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime(); using var session = runtime.Client.CreateSession();
        using var task = session.BeginTask(TestContext.Current.CancellationToken);
        var changed = AgentModelTestRuntime.First with { Endpoint = "https://changed.example/v1", ApiKey = "replacement-key", UpdatedAt = DateTimeOffset.UtcNow };
        await runtime.Catalog.SaveAsync(AgentModelTestRuntime.First, changed, true, runtime.Catalog.Epoch);
        Assert.True(task.Token.IsCancellationRequested);
        var captured = Assert.Single(runtime.Client.Captured); Assert.Equal(AgentModelTestRuntime.First.ApiKey, captured.Options.ApiKey); Assert.Equal(AgentModelTestRuntime.First.Endpoint, captured.Options.Endpoint);
        task.Dispose();
        await runtime.Catalog.DeleteAsync(changed, runtime.Catalog.Epoch);
        Assert.False(session.IsConfigured); Assert.Empty(session.SelectedId); Assert.Empty(runtime.Catalog.Current.DefaultProfileId);
        Assert.Throws<InvalidOperationException>(() => session.BeginTask(TestContext.Current.CancellationToken));
        Assert.Equal(2, session.Profiles.Count);
    });

    [Fact]
    public Task AccountChangeRevokesOldSessionAndRejectsStaleEditor() => fixture.RunOffscreenAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime(); using var session = runtime.Client.CreateSession();
        using var task = session.BeginTask(TestContext.Current.CancellationToken);
        long epoch = runtime.Catalog.Epoch; await runtime.Accounts.SignInAsync(TestContext.Current.CancellationToken);
        Assert.True(task.Token.IsCancellationRequested); Assert.False(session.IsOwnerCurrent); Assert.Empty(session.Profiles);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Catalog.SaveAsync(AgentModelTestRuntime.First,
            AgentModelTestRuntime.First with { Name = "Old editor" }, false, epoch, TestContext.Current.CancellationToken));
        Assert.Equal("AgentModelOwnerChanged", error.Data["Hermod.ResourceKey"]);
        Assert.Equal(AgentModelTestRuntime.First, runtime.Store.Load()!.Profiles[0]);
    });

    [Fact]
    public Task DeletedOrRemotelyChangedProfileCannotBeResurrectedByOldEditor() => fixture.RunOffscreenAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime(); long epoch = runtime.Catalog.Epoch;
        await runtime.Catalog.DeleteAsync(AgentModelTestRuntime.First, epoch);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Catalog.SaveAsync(AgentModelTestRuntime.First,
            AgentModelTestRuntime.First with { Name = "Stale" }, false, epoch, TestContext.Current.CancellationToken));
        Assert.Equal("AgentModelChanged", error.Data["Hermod.ResourceKey"]); Assert.Equal(2, runtime.Store.Load()!.Profiles.Count);
        var updated = AgentModelTestRuntime.Second with { Model = "remote-model" };
        runtime.Store.Save(new([updated, AgentModelTestRuntime.Third]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Catalog.DeleteAsync(AgentModelTestRuntime.Second, epoch, TestContext.Current.CancellationToken));
        Assert.Equal("remote-model", runtime.Store.Load()!.Profiles[0].Model);
    });
}
