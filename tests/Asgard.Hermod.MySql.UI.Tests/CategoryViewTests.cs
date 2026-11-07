using System.Windows.Data;
using System.Windows.Input;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class CategoryViewTests(WpfTestFixture fixture)
{
    [Fact]
    public Task ReselectingCategory_PreservesInvalidCellEdit() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql(); var config = new Configuration();
        await using var model = new MySqlWorkspaceViewModel(new(Configuration.Instance), config, server, config, new TestMySqlDialogs(), "app");
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var view = new MySqlWorkspaceView { DataContext = model }; var window = new HermodWindow { Content = view, Width = 1120, Height = 780 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var database = Assert.Single(model.SessionTreeItems); await model.Tree!.LoadAsync(database); database.IsExpanded = true;
            var category = database.Children.Single(item => item.Object.Kind == MySqlObjectKind.Tables);
            var tree = Assert.IsType<WorkspaceTreeView>(view.FindName("ObjectTree"));
            tree.RaiseEvent(new RoutedPropertyChangedEventArgs<object>(null!, category, TreeView.SelectedItemChangedEvent));
            await model.Tree.LoadAsync(category); await model.OpenAsync(Assert.Single(category.Children));
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var host = Assert.IsType<ContentControl>(view.FindName("TabHost"));
            var tab = Assert.IsType<MySqlTabView>(host.Content);
            var result = Assert.Single(WindowTestVisuals.Descendants<MySqlResultView>(tab));
            var grid = Assert.IsType<DataGrid>(result.FindName("ResultGrid"));
            grid.CurrentCell = new(grid.Items[0], grid.Columns[0]); grid.SelectedIndex = 0; grid.Focus(); grid.BeginEdit();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var input = Assert.Single(WindowTestVisuals.Descendants<TextBox>(grid), box => box.IsVisible);
            input.Text = "not an integer";
            var categoryItem = Assert.Single(WindowTestVisuals.Descendants<TreeViewItem>(tree), item => ReferenceEquals(item.DataContext, category));
            categoryItem.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(tab, host.Content);
            Assert.Equal("not an integer", input.Text);
            input.Text = "2";
            categoryItem.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.IsType<MySqlCategoryView>(host.Content);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CategorySelection_ShowsMetadataAndOpensObjectWithKeyboard() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql
        {
            MetadataOverride = parent => parent.Kind == MySqlObjectKind.Tables
            ? [new(MySqlObjectKind.Table, "users", "app") { AutoIncrement = 42, DataLength = 1024, Engine = "InnoDB", RowCount = 3, Comment = "people" }] : null
        };
        var config = new Configuration();
        await using var model = new MySqlWorkspaceViewModel(new(Configuration.Instance), config, server, config, new TestMySqlDialogs(), "app");
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var view = new MySqlWorkspaceView { DataContext = model }; var window = new HermodWindow { Content = view, Width = 1120, Height = 780 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var database = Assert.Single(model.SessionTreeItems); await model.Tree!.LoadAsync(database);
            var category = database.Children.Single(item => item.Object.Kind == MySqlObjectKind.Tables);
            var tree = Assert.IsType<WorkspaceTreeView>(view.FindName("ObjectTree"));
            tree.RaiseEvent(new RoutedPropertyChangedEventArgs<object>(null!, category, TreeView.SelectedItemChangedEvent));
            await model.Tree.LoadAsync(category); await WindowTestVisuals.UpdateLayoutAsync(window);
            var host = Assert.IsType<ContentControl>(view.FindName("TabHost"));
            var categoryView = Assert.IsType<MySqlCategoryView>(host.Content);
            var grid = Assert.IsType<DataGrid>(categoryView.FindName("ItemsGrid"));
            var row = Assert.Single(category.Children); Assert.Same(category.Children, grid.ItemsSource);
            Assert.Equal((ulong)42, row.Object.AutoIncrement); Assert.Equal("InnoDB", row.Object.Engine);
            Assert.Contains(grid.Columns.OfType<DataGridTextColumn>(), column => ((Binding)column.Binding).Path.Path == "Object.RowCount" && column.Visibility == Visibility.Visible);
            grid.SelectedItem = row;
            grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(MySqlObjectKind.Table, model.ActiveTab!.Kind);
            Assert.IsType<MySqlTabView>(host.Content);
            var opened = model.ActiveTab;

            tree.RaiseEvent(new RoutedPropertyChangedEventArgs<object>(null!, category, TreeView.SelectedItemChangedEvent));
            var tabs = Assert.IsType<WorkspaceTabStrip>(view.FindName("ObjectTabs"));
            tabs.SelectedItem = opened; await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.IsType<MySqlTabView>(host.Content);

            tree.RaiseEvent(new RoutedPropertyChangedEventArgs<object>(null!, category, TreeView.SelectedItemChangedEvent));
            categoryView = Assert.IsType<MySqlCategoryView>(host.Content);
            grid = Assert.IsType<DataGrid>(categoryView.FindName("ItemsGrid"));
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var gridRow = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(row));
            gridRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.IsType<MySqlTabView>(host.Content);
        }
        finally { window.Close(); }
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
