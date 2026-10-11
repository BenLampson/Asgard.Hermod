namespace Asgard.Hermod.UI.Tests;

/// <summary>验证导航操作与界面筛选边界，不依赖布局或网络连接。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class NavigationTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>使用集合级 STA 应用环境。</summary>
    public NavigationTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>本地实例即使排在前三项，也不能在打开应用时自动启动。</summary>
    [Fact]
    public Task Construction_DoesNotStartShell_WhenLocalInstanceIsSecond() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();

        Assert.True(environment.Source.GetInstances()[1].IsLocal);
        Assert.Equal(0, environment.Factory.OpenAttempts);
        Assert.DoesNotContain(environment.Shell.Workspace.Tabs, page => page is TerminalWorkspaceViewModel);
        Assert.Equal(new[] { "heimdall", "bridge", "tyr" }, environment.Shell.Workspace.Tabs
            .OfType<ServiceDetailViewModel>().Select(page => page.InstanceId));
    });

    /// <summary>名称和标签必须同时匹配；清除其中一项不能覆盖另一项。</summary>
    [Fact]
    public Task Sidebar_QueryAndTagMustBothMatch() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var sidebar = environment.Shell.Sidebar;
        sidebar.SelectedTag = "Production";
        sidebar.Query = "01";

        Assert.Equal("heimdall", Assert.Single(sidebar.Instances.Cast<InstanceItemViewModel>()).Id);

        sidebar.Query = "Tyr";
        Assert.True(sidebar.IsEmpty);
        sidebar.ClearTagCommand.Execute(null);
        Assert.Equal("tyr", Assert.Single(sidebar.Instances.Cast<InstanceItemViewModel>()).Id);
        Assert.Equal("Tyr", sidebar.Query);
    });

    /// <summary>全局搜索使用完整实例来源，不受侧栏当前过滤条件约束。</summary>
    [Fact]
    public Task GlobalSearch_IgnoresSidebarQueryAndTag() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var shell = environment.Shell;
        shell.Sidebar.SelectedTag = "Staging";
        shell.Sidebar.Query = "Tyr";
        shell.GlobalSearchQuery = "Heimdall";

        var result = Assert.Single(shell.GlobalSearchResults);
        Assert.Equal("heimdall", result.Id);
        Assert.Equal("tyr", Assert.Single(shell.Sidebar.Instances.Cast<InstanceItemViewModel>()).Id);

        shell.OpenGlobalSearchResultCommand.Execute(result);
        Assert.Equal("heimdall", shell.Workspace.SelectedTab.InstanceId);
        Assert.Empty(shell.GlobalSearchQuery);
        Assert.Equal("Staging", shell.Sidebar.SelectedTag);
        Assert.Equal("Tyr", shell.Sidebar.Query);
    });

    /// <summary>取消创建表单保留当前页签、导航集合和连接状态。</summary>
    [Fact]
    public Task AddInstance_CancellationKeepsNavigation() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var shell = environment.Shell;
        var originalTab = shell.Workspace.SelectedTab;
        int originalCount = shell.Sidebar.AllInstances.Count;

        await shell.Sidebar.AddInstanceCommand.ExecuteAsync();

        Assert.Equal(originalCount, shell.Sidebar.AllInstances.Count);
        Assert.Same(originalTab, shell.Workspace.SelectedTab);
        Assert.Null(shell.Sidebar.AddInstanceCommand.LastException);
        Assert.Equal(0, environment.Factory.OpenAttempts);
    });

    /// <summary>成功添加后清除旧过滤并打开新实例，但不创建本地 Shell。</summary>
    [Fact]
    public Task AddInstance_ValidEntryRefreshesFiltersAndNavigates() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var shell = environment.Shell;
        shell.Sidebar.Query = "Tyr";
        shell.Sidebar.SelectedTag = "Staging";
        environment.Editor.NextResult = new InstanceSummary("new-prod", "New-04", "New service",
            "server", ["New region"]);

        await shell.Sidebar.AddInstanceCommand.ExecuteAsync();

        Assert.Null(shell.Sidebar.AddInstanceCommand.LastException);
        Assert.Empty(shell.Sidebar.Query);
        Assert.Null(shell.Sidebar.SelectedTag);
        Assert.Contains(shell.Sidebar.AllInstances, item => item.Id == "new-prod");
        Assert.Contains("New region", shell.Sidebar.AvailableTags);
        Assert.Equal("new-prod", shell.Workspace.SelectedTab.InstanceId);
        Assert.Equal(0, environment.Factory.OpenAttempts);
    });
}
