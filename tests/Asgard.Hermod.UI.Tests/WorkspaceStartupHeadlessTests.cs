namespace Asgard.Hermod.UI.Tests;

/// <summary>首次失败、重复打开、关闭与退出竞态只使用假页面，不创建工作窗口。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceStartupHeadlessTests(WpfTestFixture fixture)
{
    [Fact]
    public Task PendingSuccess_ActivatesOnlyAfterConnectionAndCoalescesRepeatedOpens() => fixture.RunOffscreenAsync(async () =>
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Pages { Pending = pending.Task, Succeed = true };
        await using var workspace = new WorkspaceViewModel(new Source(), new Terminals(), factory);
        int activations = 0; workspace.PageActivated += _ => activations++;
        workspace.OpenInstance(new(Source.Instance)); workspace.OpenInstance(new(Source.Instance));
        Assert.Equal(0, activations); Assert.True(workspace.IsOpening); Assert.Equal(1, factory.Created);
        pending.SetResult(); await workspace.InitializePageAsync(factory.Last!); await WpfTestFixture.DrainDispatcherAsync();
        Assert.Equal(1, activations); Assert.False(workspace.IsOpening); Assert.True(factory.Last!.HasConnectedOnce);
        workspace.OpenInstance(new(Source.Instance)); Assert.Equal(2, activations); Assert.Equal(1, factory.Created);
    });
    [Fact]
    public Task SynchronousFailure_RemovesPageBeforeActivationAndReportsSafeError() => fixture.RunOffscreenAsync(async () =>
    {
        var factory = new Pages();
        await using var workspace = new WorkspaceViewModel(new Source(), new Terminals(), factory);
        int activations = 0; workspace.PageActivated += _ => activations++;
        workspace.OpenInstance(new(Source.Instance));
        Assert.Equal(0, activations); Assert.Single(workspace.Tabs); Assert.Equal(1, factory.Last!.Releases);
        Assert.True(factory.Last.IsClosed); Assert.NotEmpty(workspace.OpenError);
        Assert.DoesNotContain("private", workspace.OpenError, StringComparison.OrdinalIgnoreCase);
        workspace.OpenInstance(new(Source.Instance)); Assert.Equal(2, factory.Created);
    });

    [Fact]
    public Task RepeatedPendingOpenAndLateFailureAfterCloseCannotCreateGhostPage() => fixture.RunOffscreenAsync(async () =>
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Pages { Pending = pending.Task };
        await using var workspace = new WorkspaceViewModel(new Source(), new Terminals(), factory);
        workspace.OpenInstance(new(Source.Instance)); var page = factory.Last!;
        workspace.OpenInstance(new(Source.Instance)); Assert.Equal(1, factory.Created); Assert.Equal(1, page.Starts);
        Task initialize = workspace.InitializePageAsync(page);
        await workspace.CloseTabAsync(page); pending.SetResult(); await initialize;
        Assert.True(page.IsClosed); Assert.Equal(1, page.Releases); Assert.Single(workspace.Tabs); Assert.Empty(workspace.OpenError);
    });

    [Fact]
    public Task ExitPendingAndInitialLocalProcessFailureNeverLeaveFailedItems() => fixture.RunOffscreenAsync(async () =>
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Pages { Pending = pending.Task };
        await using var workspace = new WorkspaceViewModel(new Source(), new Terminals(), factory);
        workspace.OpenInstance(new(Source.Instance)); var page = factory.Last!; var initialize = workspace.InitializePageAsync(page);
        workspace.IsExitPending = true; pending.SetResult(); await initialize;
        Assert.Single(workspace.Tabs); Assert.True(page.IsClosed); Assert.Empty(workspace.OpenError);
        workspace.IsExitPending = false; workspace.OpenLocalTerminal();
        Assert.Single(workspace.Tabs); Assert.NotEmpty(workspace.OpenError);
    });

    [Fact]
    public Task Dispose_WaitsForPendingInitializationAndClearsItsRegistry() => fixture.RunOffscreenAsync(async () =>
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Pages { Pending = pending.Task };
        var workspace = new WorkspaceViewModel(new Source(), new Terminals(), factory);
        workspace.OpenInstance(new(Source.Instance)); var page = factory.Last!;
        Task disposal = workspace.DisposeAsync().AsTask(); Assert.True(page.IsClosed); Assert.False(disposal.IsCompleted);
        pending.SetResult(); await disposal; Assert.Empty(workspace.Tabs); Assert.Empty(workspace.OpenError);
        Assert.Equal(1, page.Releases);
        var registry = (System.Collections.IDictionary)typeof(WorkspaceViewModel).GetField("_initializations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(workspace)!;
        Assert.Empty(registry.Keys.Cast<object>());
    });

    private sealed class StartupPage(Task? pending, bool succeed) : WorkspacePageViewModel("startup", "Instance", "database", "instance")
    {
        public override WorkspaceConnectionViewModel ConnectionState { get; } = new();
        internal int Starts, Releases;
        public override async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            Starts++; if (pending is not null) await pending;
            if (succeed) { HasConnectedOnce = true; ConnectionState.Finish(); return; }
            ConnectionState.Finish(Asgard.Hermod.Abstractions.Localization.Text.Error(new IOException("private password")));
        }
        protected override ValueTask ReleaseAsync() { Releases++; return ValueTask.CompletedTask; }
    }
    private sealed class Pages : IInstanceWorkspaceFactory
    {
        internal Task? Pending; internal StartupPage? Last; internal int Created; internal bool Succeed;
        public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance) { Created++; return Last = new(Pending, Succeed); }
    }
    private sealed class Source : IWorkspaceDataSource
    {
        internal static InstanceSummary Instance => new("instance", "Instance", "Instance", "database", []);
        public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
        public ServiceSnapshot? GetServiceSnapshot(string instanceId) => null;
    }
    private sealed class Terminals : ITerminalSessionFactory
    {
        public ITerminalWorkspaceSession OpenLocal() => throw new IOException("private credentials");
    }
}
