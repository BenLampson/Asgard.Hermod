using System.Globalization;
using System.Windows.Media.Imaging;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证首页真实导航语义、双语与主题布局，不创建远程连接。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class HomeWorkspaceTests(WpfTestFixture fixture)
{
    /// <summary>第七次访问淘汰最旧记录，重复访问移动到首位并保持六条上限。</summary>
    [Fact]
    public Task RecentHistory_KeepsSixLatestUniqueInstances() => fixture.RunAsync(() =>
    {
        var home = new HomeWorkspaceViewModel(_ => { });
        var instances = Enumerable.Range(0, 7).Select(index => new InstanceItemViewModel(new("recent-" + index, "name-" + index, "name-" + index, "database", []))).ToArray();
        home.Refresh(instances); foreach (var instance in instances) home.RecordOpened(instance);
        Assert.Equal(instances.Skip(1).Reverse(), home.RecentInstances.Select(item => item.Instance));
        home.RecordOpened(instances[2]); Assert.Same(instances[2], home.RecentInstances[0].Instance); Assert.Equal(6, home.RecentInstances.Count);
        Assert.Equal(6, home.RecentInstances.Select(item => item.Instance.Id).Distinct().Count()); return Task.CompletedTask;
    });
    /// <summary>首页从安全快照计数，显式访问去重，改名与删除及时更新。</summary>
    [Fact]
    public Task OverviewAndHistory_TrackUserActionsAndCurrentSnapshots() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var home = environment.Shell.Workspace.Home;
        Assert.Same(home, environment.Shell.Workspace.SelectedTab);
        Assert.Empty(home.RecentInstances);
        Assert.Equal(4, home.Types.Where(type => type.Id != "local").Sum(type => type.Count));
        Assert.DoesNotContain(home.Types, type => type.Id == "local");
        var instance = environment.Shell.Sidebar.AllInstances.First(item => !item.IsLocal);
        environment.Shell.Workspace.OpenInstance(instance);
        environment.Shell.Workspace.OpenInstance(instance);
        Assert.Same(instance, Assert.Single(home.RecentInstances).Instance);
        var replacement = new InstanceItemViewModel(instance.Summary with { DisplayName = "Renamed by user" });
        home.Refresh([replacement]);
        Assert.Equal("Renamed by user", Assert.Single(home.RecentInstances).Instance.DisplayName);
        home.Refresh([]);
        Assert.Empty(home.RecentInstances);
        Assert.True(home.HasNoRecentInstances);
    });

    /// <summary>真实窗口下验证主题图标、双语文案、窄窗口和键盘访问入口。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Home_RendersAndNavigates(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        Window? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            await using var environment = new ShellTestEnvironment();
            environment.Themes.SetTheme(theme);
            using var host = environment.CreateContentHost();
            var view = Assert.IsType<HomeWorkspaceView>(host.Content);
            window = new Window
            {
                Content = host,
                Width = 1136,
                Height = 821,
                ShowInTaskbar = false,
                ShowActivated = false,
                Opacity = 0
            };
            window.Show();
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), block => block.Text == Text.Get("HomeTagline"));
            var logo = Assert.IsType<Image>(view.FindName("HeroLogo"));
            Assert.Same(Application.Current.Resources["HermodLogo"], logo.Source);
            Assert.True(logo.IsVisible);
            var statistics = Assert.Single(WindowTestVisuals.Descendants<HomeStatisticsPanel>(view));
            Assert.All(statistics.Children.Cast<UIElement>(), child => Assert.Equal(statistics.Children[0].RenderSize.Width, child.RenderSize.Width));
            var terminalButton = Assert.Single(WindowTestVisuals.Descendants<Button>(view),
                button => ReferenceEquals(button.Command, environment.Shell.Sidebar.OpenLocalTerminalCommand));
            Assert.True(terminalButton.Focusable);
            Capture(view, "home-" + language + "-" + theme);
            window.Width = 520;
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(logo.IsVisible);
            var scroll = Assert.Single(WindowTestVisuals.Descendants<ScrollViewer>(view), item => item.Parent is HomeWorkspaceView);
            Assert.Equal(0, scroll.ScrollableWidth);
            Capture(view, "home-narrow-" + language + "-" + theme);
            terminalButton.Command.Execute(null);
            Assert.IsType<TerminalWorkspaceViewModel>(environment.Shell.Workspace.SelectedTab);
            Assert.Single(environment.Shell.Workspace.Home.RecentInstances);
            environment.Shell.Workspace.SelectedTab = environment.Shell.Workspace.Home;
            await WpfTestFixture.DrainDispatcherAsync();
            var recent = Assert.Single(environment.Shell.Workspace.Home.RecentInstances);
            var recentButton = Assert.Single(WindowTestVisuals.Descendants<Button>(view), button => ReferenceEquals(button.CommandParameter, recent));
            Assert.Same(environment.Shell.Workspace.Home.OpenRecentCommand, recentButton.Command);
            window.Width = 1136;
            await WpfTestFixture.DrainDispatcherAsync();
            Capture(view, "home-recent-" + language + "-" + theme);
            recentButton.Command.Execute(recent);
            Assert.IsType<TerminalWorkspaceViewModel>(environment.Shell.Workspace.SelectedTab);
        }
        finally
        {
            window?.Close();
            CultureInfo.CurrentUICulture = previous;
        }
    });

    /// <summary>没有实例的已注册类型保持零计数，未知类型分组不会遗漏第二条记录。</summary>
    [Fact]
    public Task Statistics_KeepZeroTypesAndCountAllUnknownInstances() => fixture.RunAsync(() =>
    {
        var registry = new InstanceProviderRegistry([new FakeStorageProvider("test")]);
        var home = new HomeWorkspaceViewModel(_ => { }, registry);
        home.Refresh([]);
        Assert.Equal(0, Assert.Single(home.Types, type => type.Id == "storage").Count);
        Assert.DoesNotContain(home.Types, type => type.Id == "local");
        home.RecordOpened(new(new("local-shell", "sandbox-local", "Local terminal", "terminal", [], true)));
        Assert.Equal("local-shell", Assert.Single(home.RecentInstances).Instance.Id);
        home.Refresh([
            new(new("one", "one", "one", "server", [], TypeId: "future")),
            new(new("two", "two", "two", "server", [], TypeId: "future")),
            new(new("three", "three", "three", "database", [], TypeId: "storage"))
        ]);
        Assert.Equal(2, Assert.Single(home.Types, type => type.Id == "future").Count);
        Assert.Equal(1, Assert.Single(home.Types, type => type.Id == "storage").Count);
        return Task.CompletedTask;
    });

    /// <summary>实际主窗口的首页填满内容槽，能够从键盘进入快捷操作。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task MainWindow_ShowsHomeAndKeyboardActions(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        Asgard.Hermod.MainWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-Hans");
            await using var environment = new ShellTestEnvironment();
            environment.Themes.SetTheme(theme);
            window = new Asgard.Hermod.MainWindow(environment.Shell)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                Opacity = 0
            };
            window.Show();
            await WpfTestFixture.DrainDispatcherAsync();
            var home = Assert.Single(WindowTestVisuals.Descendants<HomeWorkspaceView>(window));
            var button = Assert.Single(WindowTestVisuals.Descendants<Button>(home),
                item => ReferenceEquals(item.Command, environment.Shell.Sidebar.AddInstanceCommand));
            Assert.True(button.Focus());
            Assert.True(button.IsKeyboardFocused);
            Assert.True(home.ActualWidth > 0 && home.ActualHeight > 0);
            Capture(WindowTestVisuals.Frame(window), "home-window-" + theme);
        }
        finally
        {
            window?.Close();
            await WpfTestFixture.DrainDispatcherAsync();
            CultureInfo.CurrentUICulture = previous;
        }
    });

    /// <summary>按需输出真实 WPF 渲染供人工核对。</summary>
    private static void Capture(FrameworkElement view, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
