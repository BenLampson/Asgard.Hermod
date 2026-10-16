using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>其余三种真实模块转移相同标题元素；终端和对象存储均使用假边界。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceHeaderModulesHeadlessTests(WpfTestFixture fixture)
{
    [Fact]
    public Task SshHeader_TransfersActionsWithoutReplacingTerminal() => fixture.RunOffscreenAsync(async () =>
    {
        var factory = new SshTestFactory(); var dialogs = new SshTestDialogs { Accept = true };
        await using var page = new SshWorkspaceViewModel(new(new("ssh", "SSH", "SSH", "terminal", [], TypeId: SshIds.Type, ProviderId: SshIds.Direct)), new SshTestConfigurationService(), factory, dialogs, dialogs);
        await page.InitializeAsync(); var terminal = page.Session;
        using var host = new WorkspaceContentHost { Page = page }; AssertHeader(host, page);
        Assert.Same(terminal, page.Session); Assert.True(page.IsConnected); Assert.Equal(1, factory.Calls);
    });

    [Fact]
    public Task StorageHeader_TransfersIdentityAndConnectionEditWithoutExtraReads() => fixture.RunOffscreenAsync(async () =>
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service();
        await using var page = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage);
        await page.InitializeAsync(TestContext.Current.CancellationToken); int details = service.DetailsCalls.Count;
        using var host = new WorkspaceContentHost { Page = page }; AssertHeader(host, page);
        Assert.True(page.HasConnectedOnce); Assert.Equal(details, service.DetailsCalls.Count); Assert.Empty(service.DownloadLimits);
    });

    [Fact]
    public Task LocalHeader_TransfersWithoutStartingAnotherProcess() => fixture.RunOffscreenAsync(async () =>
    {
        var factory = new FakeTerminalSessionFactory();
        await using var page = new TerminalWorkspaceViewModel("local", new(new("local", "Local", "Local", "terminal", [], true)), factory);
        var terminal = page.Session; using var host = new WorkspaceContentHost { Page = page }; AssertHeader(host, page);
        Assert.Same(terminal, page.Session); Assert.True(page.IsRunning); Assert.Equal(1, factory.OpenAttempts);
    });

    private static void AssertHeader(WorkspaceContentHost host, WorkspacePageViewModel page)
    {
        var view = Assert.IsAssignableFrom<FrameworkElement>(host.Content); var header = Assert.IsType<WorkspaceHeader>(host.TakeHeader());
        Assert.Same(page, header.DataContext); Assert.Null(host.TakeHeader());
        var root = new Border { Child = header }; root.Resources.MergedDictionaries.Add(Application.Current.Resources);
        header.ShowWindowControls = true; root.Measure(new(800, 44)); root.Arrange(new(0, 0, 800, 44)); root.UpdateLayout();
        Assert.Equal(44, header.ActualHeight); Assert.NotNull(header.IdentityContent); Assert.NotNull(header.StatusContent);
        Assert.Same(page, view.DataContext); Assert.Null(PresentationSource.FromVisual(root)); Assert.Null(Window.GetWindow(root));
    }
}
