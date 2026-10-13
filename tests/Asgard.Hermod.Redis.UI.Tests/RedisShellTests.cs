using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>在真实 AppShell 中验证模块模板和应用退出确认，不使用独立页面代替宿主。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class RedisShellTests(WpfTestFixture fixture)
{
    [Fact]
    public Task MainWindow_UsesRedisModuleTemplateAndHonorsDirtyTabCancellation() => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace();
        var resource = new ResourceDictionary { Source = new Uri("pack://application:,,,/Asgard.Hermod.Redis.UI;component/Resources/WorkspacePages.xaml") };
        Application.Current.Resources.MergedDictionaries.Add(resource);
        var configs = new RedisTestConfigurations(); var session = new RedisTestSession(); var dialogs = new RedisTestDialogs();
        var pages = new InstanceWorkspaceFactory([new RedisPageProvider(configs, session, configs, dialogs)]);
        await using var shell = new ShellViewModel(configs, configs, new RedisTestTerminalFactory(), new ThemeService(Application.Current.Resources), pageFactory: pages);
        var window = new MainWindow(shell) { WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            WindowPreviewCapture.Show(window); await shell.InitializeAsync();
            shell.Workspace.OpenInstance(Assert.Single(shell.Sidebar.AllInstances));
            var page = Assert.IsType<RedisWorkspaceViewModel>(shell.Workspace.SelectedTab);
            await page.InitializeAsync(TestContext.Current.CancellationToken); await page.OpenAsync(RedisKeyId.FromText("cache:homepage"));
            await WindowPreviewCapture.VerifyAsync(window, "redis-real-shell");
            Assert.Single(WindowTestVisuals.Descendants<RedisWorkspaceView>(window));
            await page.ActiveTab!.EditCommand.ExecuteAsync(); page.ActiveTab.EditorText = "draft";
            window.Close(); await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(window.IsVisible); Assert.True(page.ActiveTab.IsDirty); Assert.False(page.IsClosed); Assert.Equal(1, dialogs.ClosePrompts);
            dialogs.Choice = RedisCloseChoice.Discard; window.Close(); await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(page.IsClosed); Assert.False(window.IsVisible);
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { dialogs.Choice = RedisCloseChoice.Discard; window.Close(); Application.Current.Resources.MergedDictionaries.Remove(resource); }
    });
}
