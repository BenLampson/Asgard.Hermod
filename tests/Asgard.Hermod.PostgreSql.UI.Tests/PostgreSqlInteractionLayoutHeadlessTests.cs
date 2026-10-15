using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>无 Window 的真实模块布局和受保护页签操作，不发送桌面输入。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class PostgreSqlInteractionLayoutHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light, 800)]
    [InlineData("zh-Hans", UiTheme.Dark, 1200)]
    public Task QueryAndTable_ExposeActionsAndBoundedPageControls(string language, UiTheme theme, double width) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(theme);
            var server = new TestPostgreSql(); var dialogs = new TestPostgreSqlDialogs();
            await using var query = new PostgreSqlTabViewModel("Query", "app", PostgreSqlObjectKind.Root, server, TestPostgreSql.Connection, dialogs);
            var view = new PostgreSqlTabView { DataContext = query }; var root = Root(view); Layout(root, width - 280);
            Assert.IsType<WrapPanel>(view.FindName("QueryToolbar"));
            var current = (Button)view.FindName("RunCurrent"); var all = (Button)view.FindName("RunAll");
            Assert.Equal(Text.Get("PostgreSqlRunCurrentButton"), current.Content); Assert.Equal(Text.Get("PostgreSqlRunAllButton"), all.Content);
            Assert.Equal(Visibility.Visible, current.Visibility); Assert.Equal(Visibility.Visible, all.Visibility);
            Assert.True(all.TranslatePoint(new(), root).X + all.ActualWidth <= root.ActualWidth + 1);
            Assert.Equal(Visibility.Collapsed, ((Grid)view.FindName("FilterToolbar")).Visibility);
            Capture(root, $"postgresql-query-{language}-{theme}-{width}");
            query.Sql = "SELECT 1; SELECT 2;"; await query.ExecuteScopeAsync("SELECT 9", 0, false);
            Assert.Equal("SELECT 9", server.ExecutedSql.Last().Sql); Assert.Equal("app", server.ExecutedSql.Last().Database);
            await query.ExecuteScopeAsync("ignored selection", 0, true); Assert.Equal(query.Sql, server.ExecutedSql.Last().Sql);
            await using var table = new PostgreSqlTabViewModel("users", "app", PostgreSqlObjectKind.Table, server, TestPostgreSql.Connection, dialogs);
            await table.ReadAsync(); var tableView = new PostgreSqlTabView { DataContext = table }; var tableRoot = Root(tableView); Layout(tableRoot, width - 280);
            Assert.Equal(Visibility.Visible, ((Grid)tableView.FindName("FilterToolbar")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((StackPanel)tableView.FindName("PageButtons")).Visibility);
            var size = (ComboBox)tableView.FindName("PageSizeSelector"); Assert.Equal(200, size.SelectedItem);
            Assert.Equal(Visibility.Visible, size.Visibility);
            Capture(tableRoot, $"postgresql-table-{language}-{theme}-{width}");
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task MiddleClick_ClosesPointedTabThroughExistingDraftPreflight() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); var dialogs = new TestPostgreSqlDialogs(); var configs = new Configurations();
        await using var page = new PostgreSqlWorkspaceViewModel(new(Configurations.Instance), configs, server, configs, dialogs);
        await page.InitializeAsync(); var first = page.ActiveTab!; var second = page.NewQuery()!;
        first.Sql = "unsaved";
        var tabs = new Asgard.Hermod.UI.Controls.WorkspaceTabStrip { ItemsSource = page.Tabs, SelectedItem = second, CloseCommand = page.CloseTabCommand };
        var root = Root(tabs); Layout(root, 700);
        Click(first); await WpfTestFixture.DrainDispatcherAsync(); Assert.Equal(2, tabs.Items.Count); Assert.Same(second, tabs.SelectedItem);
        dialogs.Choice = PostgreSqlDraftChoice.Discard; Click(first); await WpfTestFixture.DrainDispatcherAsync(); Assert.Single(tabs.Items); Assert.Same(second, tabs.SelectedItem);
        void Click(PostgreSqlTabViewModel tab)
        {
            var item = (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(tab);
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Middle) { RoutedEvent = UIElement.PreviewMouseDownEvent, Source = item };
            typeof(Asgard.Hermod.UI.Controls.WorkspaceTabStrip).GetMethod("OnPreviewMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tabs, [args]);
            Assert.True(args.Handled);
        }
    });
    private static Border Root(UIElement view)
    { var root = new Border { Child = view }; root.Resources.MergedDictionaries.Add(Application.Current.Resources); root.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); return root; }
    private static void Layout(FrameworkElement root, double width)
    {
        root.Measure(new(width, 700)); root.Arrange(new(0, 0, width, 700)); root.UpdateLayout();
        Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
    }
    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        internal static InstanceSummary Instance => new("postgresql", "PostgreSQL", "PostgreSQL", "database", [], TypeId: PostgreSqlIds.Type, ProviderId: PostgreSqlIds.Direct);
        public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestPostgreSql.Connection);
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
    private static void Capture(FrameworkElement root, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(output);
    }
}
