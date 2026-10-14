using System.Globalization;
using System.Text.Json;
using Asgard.Hermod.Abstractions.Security;
using Asgard.Hermod.MySql;
using Asgard.Hermod.Providers.Minio;
using Asgard.Hermod.Redis;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实 WPF 主窗体使用隔离 SQLite 记录，逐语言和主题输出与设计同尺寸的验收截图。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainShellDesignTests(WpfTestFixture fixture)
{
    /// <summary>概览、全部实例、类型页、更多菜单和窄窗使用相同的真实目录。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task MainWindow_MatchesApprovedLayout(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        using var storage = new LocalStorageTestEnvironment();
        var services = new ServiceCollection().AddHermodCore().AddWindowsSecretProtection().AddObjectStorage().AddAliyunOss().AddLocalStorage(storage.CreateConfiguration())
            .AddMySql().AddRedis().AddSsh().AddSshUi(() => null).AddMinio();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var tags = provider.GetRequiredService<ITagManagementService>();
        var catalog = await tags.GetTagsAsync(TestContext.Current.CancellationToken);
        var repository = provider.GetRequiredService<ILocalConfigurationRepository>();
        var protector = provider.GetRequiredService<ISecretProtector>();
        string[] names = ["Production MySQL", "Heimdall-01", "Production Redis", "Production OSS", "Development MySQL", "MinIO Local", "Staging MySQL", "Development Redis", "Bridge-01", "Backup storage", "Test server", "Archive server"];
        for (int index = 0; index < names.Length; index++)
        {
            string type = index switch { 0 or 4 or 6 => "mysql", 1 or 8 or 10 or 11 => "ssh", 2 or 7 => "redis", _ => "storage" };
            string id = type switch { "mysql" => "mysql.direct", "ssh" => "ssh-direct", "redis" => "redis.standalone", _ => "aliyun-oss" };
            var options = new Dictionary<string, string> { ["host"] = index < 4 ? "production.internal" : "127.0.0.1", ["port"] = type == "mysql" ? "3306" : type == "redis" ? "6379" : "22", ["endpoint"] = "https://oss-cn-hangzhou.aliyuncs.com", ["user"] = "test" };
            if (index == 5) { id = "minio"; options["endpoint"] = "http://127.0.0.1:9000"; }
            repository.CreateInstance(new("design-" + index, names[index], type, id, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(options)), DateTime.UtcNow,
                [index < 4 ? "builtin.environment.production" : index == 6 ? "builtin.environment.staging" : "builtin.environment.development"]));
        }
        var visits = Enumerable.Range(0, 9).Select(index => new RecentInstance("design-" + index, DateTimeOffset.UtcNow.AddMinutes(-index * 43))).ToArray();
        provider.GetRequiredService<IRecentInstanceStore>().Save(visits);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        await using var shell = new ShellViewModel(provider.GetRequiredService<IWorkspaceDataSource>(), new FakeInstanceEditor(), new FakeTerminalSessionFactory(), themes,
            tags: tags, registry: provider.GetRequiredService<IInstanceProviderRegistry>(), recentInstances: provider.GetRequiredService<IRecentInstanceStore>());
        await shell.InitializeAsync();
        var window = new Asgard.Hermod.MainWindow(shell) { Width = 1360, Height = 860, ShowActivated = false };
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowPreviewCapture.VerifyAsync(window, $"main-overview-{language}-{theme}");
            var rowsView = Assert.IsType<ListBox>(WindowTestVisuals.Named(window, "InstanceRows"));
            var scroll = WindowTestVisuals.Descendants<ScrollViewer>(rowsView).Single();
            Assert.True(scroll.ScrollableHeight == 0, $"Overview scroll: extent={scroll.ExtentHeight}; viewport={scroll.ViewportHeight}; height={rowsView.ActualHeight}");
            Assert.Equal(6, shell.Main.Rows.Count); Assert.Equal(6, shell.Main.Navigation.Count);
            shell.Main.SelectedNavigation = shell.Main.Navigation[1];
            await WindowPreviewCapture.VerifyAsync(window, $"main-all-{language}-{theme}");
            var firstPage = shell.Main.Rows.Select(row => row.Instance.Id).ToArray();
            var nextPage = WindowTestVisuals.Descendants<Button>(window).Single(button => button.Command == shell.Main.NextPageCommand);
            Assert.True(nextPage.IsEnabled);
            ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(nextPage).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(Text.Format("MainPage", 2, 2), shell.Main.PageText);
            Assert.DoesNotContain(shell.Main.Rows, row => firstPage.Contains(row.Instance.Id));
            var previousPage = WindowTestVisuals.Descendants<Button>(window).Single(button => button.Command == shell.Main.PreviousPageCommand);
            Assert.True(previousPage.IsEnabled);
            ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(previousPage).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(firstPage, shell.Main.Rows.Select(row => row.Instance.Id));
            var more = WindowTestVisuals.Descendants<Button>(window).First(button => button.Name == "MoreButton");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WpfTestFixture.DrainDispatcherAsync();
            var menu = more.ContextMenu!; menu.UpdateLayout();
            Assert.Equal(3, menu.Items.OfType<MenuItem>().Count());
            MainMenuScreenshot.Save(window, menu, $"main-more-{language}-{theme}"); menu.IsOpen = false;
            shell.Main.SelectedNavigation = shell.Main.Navigation[2];
            Assert.All(shell.Main.Rows, row => Assert.Equal("mysql", row.Instance.Summary.TypeId));
            await WindowPreviewCapture.VerifyAsync(window, $"main-type-{language}-{theme}");
            shell.Main.SelectedNavigation = shell.Main.Navigation[1]; window.Width = 1000;
            await WindowPreviewCapture.VerifyAsync(window, $"main-narrow-{language}-{theme}");
            var account = Assert.Single(WindowTestVisuals.Descendants<AccountMenu>(window));
            Assert.True(account.IsCompact);
            var accountButton = Assert.IsType<Button>(account.FindName("AccountButton"));
            Assert.True(accountButton.ActualWidth >= 22); Assert.NotNull(accountButton.ToolTip);
            Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(accountButton)));
            Assert.True(accountButton.Focus()); Assert.True(accountButton.IsKeyboardFocused);
            repository.DeleteInstance("design-0");
            foreach (int index in Enumerable.Range(1, 11)) repository.DeleteInstance("design-" + index);
            await shell.Sidebar.RefreshAsync(); window.Width = 1360; shell.Main.SelectedNavigation = shell.Main.Navigation[0];
            await WindowPreviewCapture.VerifyAsync(window, $"main-empty-{language}-{theme}");
        }
        finally { await window.RequestExitAsync(sessionEnding: true); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });
}
