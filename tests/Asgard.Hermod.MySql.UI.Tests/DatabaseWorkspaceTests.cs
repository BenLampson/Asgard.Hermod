using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>数据库工作区只缩小导航范围，不共享父窗口连接或改变查询页签的独立上下文。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class DatabaseWorkspaceTests(WpfTestFixture fixture)
{
    /// <summary>中英文聚焦页均保留可访问的库名，收起对象树后编辑器使用释放的完整宽度。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task FocusedLayout_CanCollapseNavigationAndKeepDatabaseIdentity(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace(); var previous = CultureInfo.CurrentUICulture;
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var configuration = new Configuration();
        await using var model = new MySqlWorkspaceViewModel(new(Configuration.Instance), configuration, new TestMySql(), configuration, new TestMySqlDialogs(), "app");
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var view = new MySqlWorkspaceView { DataContext = model }; var window = new HermodWindow { Content = view, Width = 1120, Height = 780 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var tree = Assert.IsType<WorkspaceTreeView>(view.FindName("ObjectTree"));
            Assert.Equal(model.SessionTreeItems, Assert.IsAssignableFrom<IEnumerable<MySqlTreeNode>>(tree.ItemsSource));
            var panel = Assert.IsType<Border>(view.FindName("TreePanel")); Assert.True(panel.IsVisible);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(panel), label => label.Text == "app");
            var host = Assert.IsType<ContentControl>(view.FindName("TabHost")); double before = host.ActualWidth;
            var toggle = Assert.IsType<Button>(view.FindName("DrawerButton")); toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WindowTestVisuals.UpdateLayoutAsync(window); Assert.False(panel.IsVisible); Assert.True(host.ActualWidth > before + 200);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WindowTestVisuals.UpdateLayoutAsync(window); Assert.True(panel.IsVisible);
            Assert.Empty(trace.Messages);
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); CultureInfo.CurrentUICulture = previous; }
    });

    /// <summary>同窗 Session 使用独立执行连接，返回选库和打开另一库均保留旧文稿。</summary>
    [Fact]
    public Task OpenDatabase_CreatesInWindowSessionsWithIndependentConnectionsAndPreservesDrafts() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql { DatabaseNames = ["app", "reporting"] }; var configuration = new Configuration(); var dialogs = new TestMySqlDialogs();
        await using var browser = new MySqlWorkspaceViewModel(new(Configuration.Instance), configuration, server, configuration, dialogs);
        await browser.InitializeAsync(TestContext.Current.CancellationToken);
        int requests = 0; browser.WorkspaceRequested += (_, _) => requests++;
        var tree = browser.Tree!; Assert.True(browser.IsCatalog);
        await browser.OpenAsync(tree.Root.Children.Single(node => node.Object.Name == "app"));
        var first = Assert.Single(browser.Sessions); Assert.Same(first, browser.ActiveTab); Assert.Equal("app", first.Database);
        await first.ExecuteAsync("SELECT 1"); first.Sql = "SELECT pending";
        Assert.Equal(2, server.Opens); Assert.Equal("app", Assert.Single(browser.SessionTreeItems).Object.Name);
        browser.BrowseDatabasesCommand.Execute(null); Assert.True(browser.IsCatalog);
        await browser.OpenAsync(tree.Root.Children.Single(node => node.Object.Name == "reporting"));
        var second = browser.ActiveSession!; Assert.NotSame(first, second); Assert.Equal("reporting", second.Database);
        await second.ExecuteAsync("SELECT 2");
        Assert.Equal(3, server.Opens); Assert.Equal(2, browser.Sessions.Count); Assert.Equal(0, requests);
        Assert.Same(tree, browser.Tree); Assert.Equal("SELECT pending", first.Sql); Assert.Contains(first, browser.Tabs);
        Assert.False(await browser.RequestCloseAsync()); Assert.True(browser.IsConnected);
        Assert.Equal(new[] { "app", "reporting" }, server.ExecutedSql.Select(item => item.Database));
        browser.UpdateInstanceMetadata(Configuration.Instance with { Name = "Renamed" });
        Assert.Equal(Text.Format("DatabaseSessionWorkspaceTitle", "Renamed"), browser.Header);
    });

    /// <summary>新 Session 继承范围，取消选库回到原文稿，不新建工作窗口或丢失草稿。</summary>
    [Fact]
    public Task Sessions_KeepTheirDatabaseAndCancelPickerWithoutLosingDrafts() => fixture.RunOffscreenAsync(async () =>
    {
        var configuration = new Configuration();
        await using var focused = new MySqlWorkspaceViewModel(new(Configuration.Instance), configuration, new TestMySql(), configuration, new TestMySqlDialogs(), "app");
        await focused.InitializeAsync(TestContext.Current.CancellationToken);
        var first = focused.ActiveTab!; await first.ChangeDatabaseAsync("reporting"); first.Sql = "SELECT pending";
        var second = focused.NewQuery()!; Assert.Equal("app", second.Database); Assert.Equal("app", first.Database);
        Assert.NotSame(first.Scope, second.Scope);
        int requests = 0; focused.WorkspaceRequested += (_, _) => requests++;
        focused.BrowseDatabasesCommand.Execute(null); Assert.True(focused.IsCatalog);
        Assert.Equal(2, focused.Sessions.Count); Assert.Equal("SELECT pending", first.Sql);
        focused.CancelDatabasePickerCommand.Execute(null);
        Assert.False(focused.IsCatalog); Assert.Same(second, focused.ActiveTab);
        Assert.Equal("app", Assert.Single(focused.SessionTreeItems).Object.Name); Assert.Equal(0, requests);
    });

    private sealed class Configuration : IInstanceConfigurationService, IInstanceEditor
    {
        internal static InstanceSummary Instance => new("mysql", "Database", "Database", "database", [], TypeId: MySqlIds.Type);
        public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestMySql.Connection);
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
