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
            Assert.Same(model.NavigationRoot!.Children, tree.ItemsSource);
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

    /// <summary>点击数据库产生可去重的请求，子窗口连接和对象分组直接属于该库。</summary>
    [Fact]
    public Task OpenDatabase_CreatesFocusedPageWithIndependentConnectionAndPreservesDrafts() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); var configuration = new Configuration(); var dialogs = new TestMySqlDialogs();
        await using var browser = new MySqlWorkspaceViewModel(new(Configuration.Instance), configuration, server, configuration, dialogs);
        await browser.InitializeAsync(TestContext.Current.CancellationToken);
        WorkspacePageRequestEventArgs? request = null; browser.WorkspaceRequested += (_, value) => request = value;
        await browser.OpenAsync(browser.Tree!.Root.Children[0]);
        Assert.Equal("mysql.database:app", request!.ContextId);
        await using var focused = Assert.IsType<MySqlWorkspaceViewModel>(request.CreatePage());
        await focused.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, server.Opens); Assert.Equal("app", focused.FocusedDatabase);
        Assert.Equal(MySqlObjectKind.Database, focused.NavigationRoot!.Object.Kind);
        Assert.Contains(focused.NavigationRoot.Children, node => node.Object.Kind == MySqlObjectKind.Tables);
        Assert.DoesNotContain(focused.NavigationRoot.Children, node => node.Object.Kind == MySqlObjectKind.Database);
        var query = focused.ActiveTab!; Assert.Equal("app", query.Database); query.Sql = "SELECT pending";
        Assert.False(await focused.RequestCloseAsync()); Assert.Equal("SELECT pending", query.Sql);
        await browser.DisposeAsync(); Assert.True(focused.IsConnected); Assert.Contains(query, focused.Tabs);
        focused.UpdateInstanceMetadata(Configuration.Instance with { Name = "Renamed" });
        Assert.Equal(Text.Format("MySqlDatabaseWorkspaceTitle", "app", "Renamed"), focused.Header);
    });

    /// <summary>跨库文稿与切换执行上下文不改变窗口的固定数据库或其他文稿。</summary>
    [Fact]
    public Task FocusedWindow_NewQueriesKeepTheirOwnDatabaseAndReturnToInstanceBrowser() => fixture.RunOffscreenAsync(async () =>
    {
        var configuration = new Configuration();
        await using var focused = new MySqlWorkspaceViewModel(new(Configuration.Instance), configuration, new TestMySql(), configuration, new TestMySqlDialogs(), "app");
        await focused.InitializeAsync(TestContext.Current.CancellationToken);
        var first = focused.ActiveTab!; await first.ChangeDatabaseAsync("reporting");
        var second = focused.NewQuery()!; Assert.Equal("app", second.Database); Assert.Equal("reporting", first.Database);
        focused.NewCrossDatabaseQueryCommand.Execute(null); Assert.Equal("", focused.ActiveTab!.Database);
        Assert.Equal("app", focused.FocusedDatabase); Assert.Equal("app", focused.NavigationRoot!.Object.Name);
        WorkspacePageRequestEventArgs? request = null; focused.WorkspaceRequested += (_, value) => request = value;
        focused.BrowseDatabasesCommand.Execute(null); Assert.NotNull(request); Assert.Null(request.ContextId);
        await using var browser = Assert.IsType<MySqlWorkspaceViewModel>(request.CreatePage());
        Assert.False(browser.IsDatabaseFocused); Assert.Null(browser.WorkspaceContextId);
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
