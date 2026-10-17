using Asgard.Hermod.UI.Views.Shell;
using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>从真实主窗体导航到独立工作窗口，验证模块模板和应用退出确认。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class RedisShellTests(WpfTestFixture fixture)
{
    [Fact]
    public Task MainWindow_UsesRedisModuleTemplateAndHonorsDirtyTabCancellation() => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace();
        var resource = new ResourceDictionary { Source = new Uri("pack://application:,,,/Asgard.Hermod.Redis.UI;component/Resources/WorkspacePages.xaml") };
        Application.Current.Resources.MergedDictionaries.Add(resource);
        var configs = new RedisTestConfigurations(); var session = new RedisTestSession
        {
            Snapshot = new("string", 169, 240000, [new([], "{\"version\":12,\"title\":\"Autumn collection\",\"enabled\":true,\"items\":[{\"id\":\"sku-1042\",\"stock\":28},{\"id\":\"sku-1089\",\"stock\":64}]}"u8.ToArray())], true),
            Scan = (_, _, _) => Task.FromResult(new RedisScanPage("0", [RedisKeyId.FromText("cache:homepage"), RedisKeyId.FromText("user:1042:profile"), RedisKeyId.FromText("events:orders"), RedisKeyId.FromText("leaderboard:weekly")]))
        }; var dialogs = new RedisTestDialogs();
        var pages = new InstanceWorkspaceFactory([new RedisPageProvider(configs, session, configs, dialogs)]);
        await using var shell = new ShellViewModel(configs, configs, new RedisTestTerminalFactory(), new ThemeService(Application.Current.Resources), pageFactory: pages);
        using var host = new WorkspaceWindowHost(shell.Workspace);
        var window = new MainWindow(shell, host, confirmExit: _ => Task.FromResult(true)) { WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            WindowPreviewCapture.Show(window); await shell.InitializeAsync();
            shell.Sidebar.OpenInstanceCommand.Execute(Assert.Single(shell.Sidebar.AllInstances));
            var page = Assert.IsType<RedisWorkspaceViewModel>(shell.Workspace.SelectedTab);
            var work = Assert.Single(host.Windows); Assert.Null(work.Owner); Assert.Same(page, work.Page);
            await page.InitializeAsync(TestContext.Current.CancellationToken); await page.OpenAsync(RedisKeyId.FromText("cache:homepage"));
            var homepage = page.ActiveTab!;
            await page.OpenAsync(RedisKeyId.FromText("user:1042:profile")); page.ActiveTab = homepage;
            var cache = page.Tree!.Root.Children.Single(node => node.Prefix == "cache:");
            await page.Tree.LoadAsync(cache); cache.IsExpanded = true;
            await WindowPreviewCapture.VerifyAsync(work, "redis-real-shell");
            Assert.Single(WindowTestVisuals.Descendants<RedisWorkspaceView>(work));
            Assert.Single(WindowTestVisuals.Descendants<MainWorkspaceView>(window));
            shell.Sidebar.OpenInstanceCommand.Execute(Assert.Single(shell.Sidebar.AllInstances));
            Assert.Same(work, Assert.Single(host.Windows));
            await page.ActiveTab!.EditCommand.ExecuteAsync(); page.ActiveTab.EditorText = "draft";
            window.Close(); await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(window.IsVisible); Assert.True(work.IsVisible); Assert.True(page.ActiveTab.IsDirty); Assert.False(page.IsClosed); Assert.Equal(1, dialogs.ClosePrompts);
            dialogs.Choice = RedisCloseChoice.Discard; window.Close(); await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(page.IsClosed); Assert.False(window.IsVisible); Assert.False(work.IsVisible);
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { dialogs.Choice = RedisCloseChoice.Discard; await window.RequestExitAsync(sessionEnding: true); Application.Current.Resources.MergedDictionaries.Remove(resource); }
    });
}
