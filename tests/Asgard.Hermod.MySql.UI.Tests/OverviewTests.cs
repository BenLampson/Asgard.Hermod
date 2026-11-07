using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>覆盖概要文档、对象刷新与会话之间的实际交互。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class OverviewTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", "Overview")]
    [InlineData("zh-Hans", "概要")]
    [InlineData("fr-FR", "Overview")]
    public Task Overview_CanReturnFromObjectCloseAndReopen(string language, string title) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var config = new Configurations(); var server = new TestMySql();
        await using var model = new MySqlWorkspaceViewModel(new(Configurations.Instance), config, server, config, new TestMySqlDialogs(), "app");
        await model.InitializeAsync();
        var session = Assert.Single(model.Sessions); var overview = Assert.Single(model.ActiveDocuments);
        Assert.True(overview.IsOverview); Assert.Same(overview, model.ActiveTab); Assert.Equal(title, overview.DocumentTitle);
        Assert.DoesNotContain(model.ActiveDocuments, item => item.IsQuery); Assert.Equal(1, server.Opens);
        var category = Assert.IsType<MySqlTreeNode>(session.OverviewCategory); Assert.Equal(MySqlObjectKind.Tables, category.Object.Kind);
        var view = new MySqlWorkspaceView { DataContext = model }; var window = new HermodWindow { Content = view, Width = 1120, Height = 780 };
        try
        {
            using var trace = new WpfBindingTrace();
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var host = (ContentControl)view.FindName("TabHost"); var tabs = (WorkspaceTabStrip)view.FindName("ObjectTabs");
            var categoryView = Assert.IsType<MySqlCategoryView>(host.Content);
            Assert.Same(category.Children, ((DataGrid)categoryView.FindName("ItemsGrid")).ItemsSource);
            Assert.True(((Button)categoryView.FindName("RefreshButton")).IsEnabled);
            await model.OpenObjectAsync(Assert.Single(category.Children).Object); var table = model.ActiveTab!;
            Assert.IsType<MySqlTabView>(host.Content); Assert.Equal(2, model.ActiveDocuments.Count);
            tabs.SelectedItem = overview; await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(categoryView, host.Content); Assert.Same(overview, model.ActiveTab);
            tabs.SelectedItem = table; await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(table, model.ActiveTab); Assert.IsType<MySqlTabView>(host.Content);
            await model.CloseTabCommand.ExecuteAsync(table); await model.CloseTabCommand.ExecuteAsync(overview);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Empty(model.ActiveDocuments); Assert.Null(model.ActiveTab); Assert.Same(session, model.ActiveSession); Assert.False(model.IsCatalog);
            model.ShowOverviewCommand.Execute(null); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(model.ActiveTab!.IsOverview); Assert.Same(category, session.OverviewCategory); Assert.IsType<MySqlCategoryView>(host.Content);
            model.ShowScriptCommand.Execute(null); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(session, model.ActiveTab); Assert.IsType<MySqlTabView>(host.Content); Assert.Single(model.Sessions);
            Assert.Empty(trace.Messages);
        }
        finally { window.Close(); Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task Refresh_ReloadsShownGroupPreservesSearchAndSafeFailureThenRebindsAfterReconnect() => fixture.RunOffscreenAsync(async () =>
    {
        string[] names = ["users"];
        var server = new TestMySql { MetadataOverride = parent => parent.Kind == MySqlObjectKind.Tables ? names.Select(name => new MySqlObject(MySqlObjectKind.Table, name, "app")).ToArray() : null };
        var config = new Configurations();
        await using var model = new MySqlWorkspaceViewModel(new(Configurations.Instance), config, server, config, new TestMySqlDialogs(), "app");
        await model.InitializeAsync(); var session = model.ActiveSession!; var category = session.OverviewCategory!;
        Assert.Single(category.Children); model.Search = "users";
        model.SelectedNode = category.Parent!.Children.Single(item => item.Object.Kind == MySqlObjectKind.Views);
        names = ["users", "new_table"];
        await model.RefreshOverviewCommand.ExecuteAsync();
        Assert.Equal(new[] { "users", "new_table" }, category.Children.Select(item => item.Object.Name));
        Assert.False(category.Children.Single(item => item.Object.Name == "new_table").IsVisible); Assert.Equal("users", model.Search);
        server.ListFailure = new IOException("password=private-test-secret"); names = ["another"];
        await model.RefreshOverviewCommand.ExecuteAsync();
        Assert.Equal(2, category.Children.Count); Assert.True(category.IsLoadFailed); Assert.DoesNotContain("private-test-secret", category.Status);
        server.ListFailure = null; model.Search = "";
        await model.RefreshOverviewCommand.ExecuteAsync(); Assert.Equal("another", Assert.Single(category.Children).Object.Name);
        model.SelectedNode = category.Parent; await model.RefreshTreeCommand.ExecuteAsync();
        var rebound = session.OverviewCategory!; Assert.NotSame(category, rebound); Assert.Equal("another", Assert.Single(rebound.Children).Object.Name);
        await model.ReconnectCommand.ExecuteAsync();
        Assert.NotSame(rebound, session.OverviewCategory); Assert.Equal("another", Assert.Single(session.OverviewCategory!.Children).Object.Name);
        Assert.True(model.ActiveTab!.IsOverview);
    });

    [Fact]
    public Task SwitchingSessions_RestoresEachOverviewAndScriptIndependently() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations();
        await using var model = new MySqlWorkspaceViewModel(new(Configurations.Instance), config, new TestMySql(), config, new TestMySqlDialogs(), "app");
        await model.InitializeAsync(); var first = model.ActiveSession!; var firstOverview = model.ActiveTab!;
        var second = model.NewQuery()!; second.Sql = "SELECT 1"; Assert.Same(second, model.ActiveTab);
        model.ActiveSession = first; Assert.Same(firstOverview, model.ActiveTab); Assert.DoesNotContain(first.Documents, item => item.IsQuery);
        model.ActiveSession = second; Assert.Same(second, model.ActiveTab); Assert.Equal("SELECT 1", second.Sql);
        model.ShowOverviewCommand.Execute(null); Assert.Same(second, model.ActiveSession); Assert.True(model.ActiveTab!.IsOverview);
        model.ActiveSession = first; Assert.Same(firstOverview, model.ActiveTab);
    });

    [Fact]
    public Task NewSession_WaitsForAnExistingDatabaseLoadBeforeChoosingTables() => fixture.RunOffscreenAsync(async () =>
    {
        var pending = new TaskCompletionSource<MySqlObjectPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestMySql { DatabaseNames = ["app", "reports"] };
        server.AsyncMetadataPageOverride = (parent, _) => parent.Kind == MySqlObjectKind.Database && parent.Name == "app" ? pending.Task : null;
        var config = new Configurations();
        await using var model = new MySqlWorkspaceViewModel(new(Configurations.Instance), config, server, config, new TestMySqlDialogs());
        await model.InitializeAsync();
        var database = model.Tree!.Root.Children.Single(item => item.Object.Name == "app");
        var loading = model.Tree.LoadAsync(database); Assert.True(database.IsLoading);
        model.OpenDatabase("app"); var session = model.ActiveSession!; Assert.Null(session.OverviewCategory);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(session.OverviewCategory) && session.OverviewCategory is not null) ready.TrySetResult(); };
        pending.SetResult(new([new(MySqlObjectKind.Tables, "", "app")], false));
        await loading; await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MySqlObjectKind.Tables, session.OverviewCategory!.Object.Kind); Assert.True(model.ActiveTab!.IsOverview);
    });

    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        internal static InstanceSummary Instance => new("mysql", "MySQL", "MySQL", "database", [], TypeId: MySqlIds.Type);
        public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestMySql.Connection);
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
