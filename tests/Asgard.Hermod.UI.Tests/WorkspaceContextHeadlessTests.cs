namespace Asgard.Hermod.UI.Tests;

/// <summary>模块聚焦工作区通过实际公共注册表去重，并保留独立的关闭与退出边界。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceContextHeadlessTests(WpfTestFixture fixture)
{
    /// <summary>重复打开只激活已有上下文；关闭实例入口不会关闭已打开的数据库工作区。</summary>
    [Fact]
    public Task ContextRequest_ReusesPageAndKeepsParentAndChildLifetimesIndependent() => fixture.RunOffscreenAsync(async () =>
    {
        var source = new TestWorkspaceDataSource(); var pages = new Pages(); var instance = new InstanceItemViewModel(source.GetInstances()[0]);
        await using var workspace = new WorkspaceViewModel(source, new FakeTerminalSessionFactory(), pages);
        workspace.OpenInstance(instance); var browser = Assert.IsType<ContextPage>(workspace.SelectedTab);
        browser.Open("database:app"); var app = Assert.IsType<ContextPage>(workspace.SelectedTab);
        app.Draft = "SELECT pending";
        browser.Open("database:app"); Assert.Same(app, workspace.SelectedTab); Assert.Equal(1, browser.CreatedChildren);
        browser.Open("database:reporting"); var reporting = Assert.IsType<ContextPage>(workspace.SelectedTab);
        Assert.NotSame(app, reporting); Assert.Equal(2, browser.CreatedChildren);
        await workspace.CloseTabAsync(browser); Assert.True(browser.IsClosed); Assert.False(app.IsClosed); Assert.False(reporting.IsClosed);
        workspace.OpenInstance(instance); var reopened = Assert.IsType<ContextPage>(workspace.SelectedTab);
        Assert.Null(reopened.WorkspaceContextId); Assert.NotSame(browser, reopened); Assert.Equal("SELECT pending", app.Draft);
        app.Open(null); Assert.Same(reopened, workspace.SelectedTab); Assert.Equal(0, app.CreatedChildren);
    });

    /// <summary>取消草稿关闭和退出预检会保留上下文；已关闭页面不能再发起幽灵窗口。</summary>
    [Fact]
    public Task ContextRequest_RespectsCloseCancellationExitPreflightAndUnsubscription() => fixture.RunOffscreenAsync(async () =>
    {
        var source = new TestWorkspaceDataSource(); var instance = new InstanceItemViewModel(source.GetInstances()[0]);
        await using var workspace = new WorkspaceViewModel(source, new FakeTerminalSessionFactory(), new Pages());
        workspace.OpenInstance(instance); var browser = Assert.IsType<ContextPage>(workspace.SelectedTab);
        browser.Open("database:app"); var app = Assert.IsType<ContextPage>(workspace.SelectedTab);
        app.Draft = "unsaved"; await workspace.CloseTabAsync(app); Assert.False(app.IsClosed); Assert.Contains(app, workspace.Tabs);
        workspace.IsExitPending = true; browser.Open("database:other"); Assert.Equal(1, browser.CreatedChildren);
        workspace.IsExitPending = false; app.Draft = ""; await workspace.CloseTabAsync(app); Assert.Equal(1, app.Releases);
        app.Open("database:ghost"); Assert.Equal(0, app.CreatedChildren);
        browser.Open("database:app"); Assert.NotSame(app, workspace.SelectedTab); Assert.Equal(2, browser.CreatedChildren);
    });

    private sealed class Pages : IInstanceWorkspaceFactory
    {
        public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance) => new ContextPage(instance.Id, null);
    }

    private sealed class ContextPage(string instanceId, string? contextId) : WorkspacePageViewModel(Guid.NewGuid().ToString("N"), "Workspace", "database", instanceId)
    {
        public override string? WorkspaceContextId => contextId;
        internal int CreatedChildren, Releases;
        internal string Draft = "";
        internal void Open(string? context) => RequestWorkspace(context, () => { CreatedChildren++; return new ContextPage(InstanceId!, context); });
        public override Task<bool> RequestCloseAsync() => Task.FromResult(Draft.Length == 0);
        protected override ValueTask ReleaseAsync() { Releases++; return ValueTask.CompletedTask; }
    }
}
