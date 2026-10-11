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
            new ThemeService(Application.Current.Resources), pageFactory: factory, storage: service);
        Assert.Equal(0, service.BucketReads);
        var item = Assert.Single(shell.Sidebar.AllInstances);
        shell.Workspace.OpenInstance(item);
        var page = Assert.IsType<ObjectStorageWorkspaceViewModel>(shell.Workspace.SelectedTab);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal("已连接", page.Status);
        Assert.NotNull(shell.Sidebar.ActiveStorageNavigation);
        Assert.Equal(item.Id, shell.Sidebar.ActiveStorageNavigation.InstanceId);
        Assert.True(item.IsSelected);

        shell.Workspace.OpenInstance(item);
        Assert.Same(page, shell.Workspace.SelectedTab);
        Assert.Equal(1, service.BucketReads);
        Assert.Single(shell.Workspace.Tabs.OfType<ObjectStorageWorkspaceViewModel>());
        await shell.Workspace.CloseTabAsync(page);
        Assert.True(page.IsClosed);
        Assert.Null(shell.Sidebar.ActiveStorageNavigation);
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

    /// <summary>侧栏对象组件与工作页绑定共享模型，页面缓存保留同一视图。</summary>
    [Fact]
    public Task ContentHost_UsesObjectBrowserForSavedStoragePage() => fixture.RunAsync(async () =>
    {
        var source = new ObjectStorageWorkspaceDataSource();
        var service = new ObjectStorageTestService();
        var editor = new FakeInstanceEditor();
        var factory = new InstanceWorkspaceFactory([new ObjectStoragePageProvider(service, new ObjectStorageTestDialogs(), editor)]);
        await using var shell = new ShellViewModel(source, editor, new FakeTerminalSessionFactory(),
            new ThemeService(Application.Current.Resources), pageFactory: factory, storage: service);
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
}
