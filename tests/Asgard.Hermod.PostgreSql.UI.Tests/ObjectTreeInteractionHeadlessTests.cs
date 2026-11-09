using System.Globalization;
using System.Windows.Input;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>验证对象树的离屏鼠标、键盘和本地化行为。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectTreeInteractionHeadlessTests(WpfTestFixture fixture)
{
    /// <summary>单击只选择对象；双击和 Enter 打开它，空分类只显示数量，错误仍可重试。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public Task ObjectTree_SelectsWithoutOpeningAndShowsOnlyUsefulStatus(string language) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var server = new TestPostgreSql(); var config = new Configuration();
            await using var model = new PostgreSqlWorkspaceViewModel(new(Configuration.Instance), config, server, config, new TestPostgreSqlDialogs(), "app");
            await model.InitializeAsync(TestContext.Current.CancellationToken);
            var database = Assert.Single(model.SessionTreeItems); await model.Tree!.LoadAsync(database); database.IsExpanded = true;
            var schema = database.Children.Single(node => node.Object.Kind == PostgreSqlObjectKind.Schema && node.Object.Name == "public");
            await model.Tree.LoadAsync(schema); schema.IsExpanded = true;
            var categories = schema.Children;
            var tables = categories.Single(node => node.Object.Kind == PostgreSqlObjectKind.Tables);
            var views = categories.Single(node => node.Object.Kind == PostgreSqlObjectKind.Views);
            await model.Tree.LoadAsync(tables); tables.IsExpanded = true;
            await model.Tree.LoadAsync(views); views.IsExpanded = true;
            Assert.Empty(views.Children); Assert.False(views.HasStatus); Assert.Empty(views.Status);
            Assert.Equal(language == "zh-Hans" ? "视图 · 0" : "Views · 0", views.DisplayLabel);
            Assert.Equal(language == "zh-Hans" ? "双击左侧对象树中的表，或新建查询继续操作。" : "Double-click a table in the object tree or create a query to continue.", Text.Get("PostgreSqlNoOpenTabsHint"));

            var view = new PostgreSqlWorkspaceView { DataContext = model }; var root = new Border { Child = view };
            root.Resources.MergedDictionaries.Add(Application.Current.Resources);
            root.Measure(new(1200, 800)); root.Arrange(new(0, 0, 1200, 800)); root.UpdateLayout();
            await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
            var panel = Assert.IsType<Border>(view.FindName("TreePanel"));
            var labels = WindowTestVisuals.Descendants<TextBlock>(panel).Select(label => label.Text).ToArray();
            Assert.DoesNotContain(language == "zh-Hans" ? "此分支没有对象。" : "No objects in this branch.", labels);
            Assert.DoesNotContain(language == "zh-Hans" ? "1 张表 · 0 个视图" : "1 tables · 0 views", labels);

            var table = Assert.Single(tables.Children);
            var item = Assert.Single(WindowTestVisuals.Descendants<TreeViewItem>(view), element => ReferenceEquals(element.DataContext, table));
            var label = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(item), element => element.Text == table.DisplayLabel);
            var previous = model.ActiveTab; int reads = server.PageReads.Count;
            var single = MouseDown(MouseButton.Left, 1);
            label.RaiseEvent(single); item.IsSelected = true;
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Same(table, model.SelectedNode); Assert.Same(previous, model.ActiveTab); Assert.Equal(reads, server.PageReads.Count);
            Assert.False(table.IsExpanded);

            var right = MouseDown(MouseButton.Right, 2);
            label.RaiseEvent(right); Assert.Same(previous, model.ActiveTab);
            var twice = MouseDown(MouseButton.Left, 2);
            label.RaiseEvent(twice); await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
            Assert.True(twice.Handled); Assert.Equal(PostgreSqlObjectKind.Table, model.ActiveTab!.Kind);
            Assert.Equal(reads + 1, server.PageReads.Count); Assert.False(table.IsExpanded);
            var opened = model.ActiveTab; model.ActiveTab = previous;
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, new NoWindowSource(), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            item.RaiseEvent(enter); await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(enter.Handled); Assert.Same(opened, model.ActiveTab); Assert.Equal(reads + 1, server.PageReads.Count);

            server.ListFailure = new IOException("private connection details");
            await model.Tree.RefreshAsync(views); Assert.True(views.IsLoadFailed); Assert.NotEmpty(views.Status);
            Assert.DoesNotContain("private connection details", views.Status);
            server.ListFailure = null; await model.Tree.RefreshAsync(views);
            Assert.False(views.IsLoadFailed); Assert.False(views.HasStatus); Assert.Empty(views.Status);
            Assert.Null(PresentationSource.FromVisual(root)); Assert.Null(Window.GetWindow(root));
        }
        finally { Text.SetApplicationCulture(null); }
    });

    // WPF 的点击次数只能由输入栈设置；离屏测试通过同一只读属性模拟路由事件。
    private static MouseButtonEventArgs MouseDown(MouseButton button, int count)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, count);
        return args;
    }

    /// <summary>只提供键盘事件的呈现来源，不注册窗口。</summary>
    private sealed class NoWindowSource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private sealed class Configuration : IInstanceConfigurationService, IInstanceEditor
    {
        internal static InstanceSummary Instance => new("test", "Database", "Database", "database", [], TypeId: PostgreSqlIds.Type, ProviderId: PostgreSqlIds.Direct);
        public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestPostgreSql.Connection);
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
