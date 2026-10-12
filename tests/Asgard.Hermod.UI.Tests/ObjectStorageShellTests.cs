namespace Asgard.Hermod.UI.Tests;

/// <summary>验证已保存实例接入真实工作页，外壳不再展示未配置占位。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageShellTests(WpfTestFixture fixture)
{
    /// <summary>页签构造不请求网络，用户选择后懒加载，并为同一实例复用浏览器。</summary>
    [Fact]
    public Task SelectSavedInstance_OpensBrowserLazilyAndReusesPage() => fixture.RunAsync(async () =>
    {
        var source = new ObjectStorageWorkspaceDataSource();
        var editor = new FakeInstanceEditor();
        var service = new ObjectStorageTestService();
        var factory = new InstanceWorkspaceFactory([new ObjectStoragePageProvider(service, new ObjectStorageTestDialogs(), editor)]);
        await using var shell = new ShellViewModel(source, editor, new FakeTerminalSessionFactory(),
            new ThemeService(Application.Current.Resources), pageFactory: factory);
        Assert.Equal(0, service.BucketReads);
        var item = Assert.Single(shell.Sidebar.AllInstances);
        shell.Workspace.OpenInstance(item);
        var page = Assert.IsType<ObjectStorageWorkspaceViewModel>(shell.Workspace.SelectedTab);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal("已连接", page.Status);
        Assert.Equal(item.Id, page.InstanceId);
        Assert.True(item.IsSelected);
        Assert.Single(service.ListCalls);

        shell.Workspace.OpenInstance(item);
        Assert.Same(page, shell.Workspace.SelectedTab);
        Assert.Equal(1, service.BucketReads);
        Assert.Single(shell.Workspace.Tabs.OfType<ObjectStorageWorkspaceViewModel>());
        await shell.Workspace.CloseTabAsync(page);
        Assert.True(page.IsClosed);
        Assert.False(item.IsSelected);
    });

    /// <summary>提供者按顶层类型分派，第三方服务商不会被外壳硬编码。</summary>
    [Fact]
    public Task WorkspaceFactory_UsesTypeForFutureDockerAndUnknownTypesFallBack() => fixture.RunAsync(async () =>
    {
        var provider = new TestInstancePageProvider("docker");
        var factory = new InstanceWorkspaceFactory([provider]);
        var docker = new InstanceItemViewModel(new("docker-1", "容器", "容器", "container", [],
            TypeId: "docker", ProviderId: "engine"));
        await using var page = factory.CreatePage(docker);
        Assert.IsType<ServiceDetailViewModel>(page);
        Assert.Equal(1, provider.CreatedPages);
        Assert.Null(factory.CreatePage(new InstanceItemViewModel(new("unknown", "其他", "其他", "server", [],
            TypeId: "future", ProviderId: "engine"))));
        Assert.Null(factory.CreatePage(new InstanceItemViewModel(new("missing", "未配置", "未配置", "server", []))));
        Assert.Equal(1, provider.CreatedPages);
    });

    /// <summary>对象浏览器仅由右侧工作区承载，页面缓存保留同一视图。</summary>
    [Fact]
    public Task ContentHost_UsesObjectBrowserForSavedStoragePage() => fixture.RunAsync(async () =>
    {
        var source = new ObjectStorageWorkspaceDataSource();
        var service = new ObjectStorageTestService();
        var editor = new FakeInstanceEditor();
        var factory = new InstanceWorkspaceFactory([new ObjectStoragePageProvider(service, new ObjectStorageTestDialogs(), editor)]);
        await using var shell = new ShellViewModel(source, editor, new FakeTerminalSessionFactory(),
            new ThemeService(Application.Current.Resources), pageFactory: factory);
        shell.Workspace.OpenInstance(shell.Sidebar.AllInstances[0]);
        var page = Assert.IsType<ObjectStorageWorkspaceViewModel>(shell.Workspace.SelectedTab);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        using var host = new WorkspaceContentHost { Page = page };
        Assert.IsType<ObjectStorageView>(host.Content);
        var currentView = host.Content;
        host.Page = shell.Workspace.Tabs[0];
        host.Page = page;
        Assert.Same(currentView, host.Content);
        await shell.Workspace.CloseTabAsync(page);
        Assert.Null(host.Content);
    });

    /// <summary>编辑已打开的连接会刷新左侧实例摘要，并保留当前页和实例选中态。</summary>
    [Fact]
    public Task SavedEdit_RefreshesInstanceSidebarWithoutReopeningBrowser() => fixture.RunAsync(async () =>
    {
        var source = new ObjectStorageWorkspaceDataSource();
        var editor = new ObjectStorageTestInstanceEditor();
        var service = new ObjectStorageTestService();
        var factory = new InstanceWorkspaceFactory([new ObjectStoragePageProvider(service, new ObjectStorageTestDialogs(), editor)]);
        await using var shell = new ShellViewModel(source, editor, new FakeTerminalSessionFactory(),
            new ThemeService(Application.Current.Resources), pageFactory: factory);
        var item = Assert.Single(shell.Sidebar.AllInstances);
        shell.Sidebar.OpenInstanceCommand.Execute(item);
        var page = Assert.IsType<ObjectStorageWorkspaceViewModel>(shell.Workspace.SelectedTab);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        editor.Result = source.Instance with { Name = "生产 OSS", DisplayName = "生产 OSS", Tags = ["Production"] };
        source.Instance = editor.Result;
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.Sidebar.InstancesChanged += (_, _) => refreshed.TrySetResult();

        await page.EditConnectionCommand.ExecuteAsync();
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Same(item, Assert.Single(shell.Sidebar.AllInstances));
        Assert.Same(page, shell.Workspace.SelectedTab);
        Assert.True(item.IsSelected);
        Assert.Equal("生产 OSS", item.Name);
        Assert.Equal("Production", Assert.Single(item.Tags));
        Assert.Equal(item.DisplayName, page.Header);
        Assert.Equal(2, service.BucketReads);
    });
}
