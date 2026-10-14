using System.Globalization;
using System.Windows.Controls.Primitives;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceLayoutTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Workspace_UsesNativeGridScrollAndCachedTabsInBothLanguages(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace(); var culture = CultureInfo.CurrentUICulture; var defaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture); Text.SetApplicationCulture(Text.Culture); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var server = new TestMySql(); var configurations = new Configurations();
        await using var model = new MySqlWorkspaceViewModel(new(new("mysql-test", "MySQL", "MySQL", "database", [], TypeId: MySqlIds.Type, ProviderId: MySqlIds.Direct)), configurations, server, configurations, new TestMySqlDialogs());
        var view = new MySqlWorkspaceView { DataContext = model }; var window = new HermodWindow { Width = 1200, Height = 800, Title = "MySQL", Content = view };
        try
        {
            WindowPreviewCapture.Show(window); await model.InitializeAsync(TestContext.Current.CancellationToken);
            var database = model.Tree!.Root.Children[0]; await model.Tree.LoadAsync(database); database.IsExpanded = true; await model.Tree.LoadAsync(database.Children[0]); database.Children[0].IsExpanded = true;
            await model.OpenObjectAsync(new(MySqlObjectKind.Table, "users", "app")); var table = model.ActiveTab!;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var tree = Assert.IsType<WorkspaceTreeView>(view.FindName("ObjectTree"));
            var databaseItem = Assert.IsType<WorkspaceTreeViewItem>(tree.ItemContainerGenerator.ContainerFromItem(database));
            var groupItem = Assert.IsType<WorkspaceTreeViewItem>(databaseItem.ItemContainerGenerator.ContainerFromItem(database.Children[0]));
            var objectItem = Assert.IsType<WorkspaceTreeViewItem>(groupItem.ItemContainerGenerator.ContainerFromItem(database.Children[0].Children[0]));
            objectItem.IsSelected = true; Assert.True(objectItem.Focus()); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(databaseItem.IsKeyboardFocusWithin); Assert.True(groupItem.IsKeyboardFocusWithin);
            Assert.False(databaseItem.IsSelected); Assert.False(groupItem.IsSelected);
            foreach (var parent in new[] { databaseItem, groupItem })
            {
                var row = Assert.IsType<Border>(parent.Template.FindName("Row", parent));
                Assert.Equal(0, Assert.IsType<SolidColorBrush>(row.BorderBrush).Color.A);
                Assert.Equal(0, Assert.IsType<SolidColorBrush>(row.Background).Color.A);
            }
            Assert.Same(Application.Current.Resources["AccentBrush"], Assert.IsType<Border>(objectItem.Template.FindName("Row", objectItem)).BorderBrush);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<IconView>(Assert.IsType<Border>(view.FindName("TreePanel"))), icon => icon.Kind == "plus");
            Assert.Equal(Text.Get("MySqlOpenKnownObject"), Assert.IsType<MenuItem>(Assert.Single(tree.ContextMenu.Items.Cast<object>())).Header);
            var emptyBranch = database.Children[1]; await model.Tree.LoadAsync(emptyBranch); emptyBranch.IsExpanded = true;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var emptyItem = Assert.IsType<WorkspaceTreeViewItem>(databaseItem.ItemContainerGenerator.ContainerFromItem(emptyBranch));
            Assert.Empty(emptyBranch.Children); Assert.False(emptyBranch.HasMore);
            Assert.Equal(Visibility.Hidden, Assert.IsType<ToggleButton>(emptyItem.Template.FindName("Expander", emptyItem)).Visibility);
            await WindowPreviewCapture.VerifyAsync(window, $"mysql-table-{language}-{theme}");
            var tableView = Assert.IsType<MySqlTabView>(Assert.IsType<ContentControl>(view.FindName("TabHost")).Content);
            var context = Assert.IsType<Grid>(tableView.FindName("ObjectContext"));
            Assert.True(context.IsVisible); Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(context), text => text.Text == "app");
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(context), text => text.Text == "users");
            VerifyDataActions(tableView, table, true);
            Assert.Equal(table.Status, Assert.IsType<TextBlock>(tableView.FindName("FooterStatus")).Text);
            table.Section = "structure"; await WindowTestVisuals.UpdateLayoutAsync(window);
            var structure = Assert.IsType<DataGrid>(tableView.FindName("StructureGrid"));
            Assert.Equal(4, structure.Items.Count); Assert.True(structure.ActualHeight > 250);
            Assert.Equal(DataGridGridLinesVisibility.Horizontal, structure.GridLinesVisibility);
            Assert.False(Assert.IsType<Grid>(tableView.FindName("DataActions")).IsVisible);
            await WindowPreviewCapture.VerifyAsync(window, $"mysql-structure-{language}-{theme}");
            table.StructureSection = "definition"; await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.False(structure.IsVisible); var definition = Assert.IsType<CodeEditor>(tableView.FindName("DefinitionEditor"));
            Assert.True(definition.IsVisible); Assert.True(definition.ActualHeight > 250);
            await WindowPreviewCapture.VerifyAsync(window, $"mysql-definition-{language}-{theme}");
            table.StructureSection = "columns"; table.Section = "data"; await WindowTestVisuals.UpdateLayoutAsync(window);
            var grid = Assert.Single(WindowTestVisuals.Descendants<DataGrid>(view), candidate => candidate.Name == "ResultGrid");
            Assert.Equal(44, grid.ColumnHeaderHeight); Assert.True(grid.EnableColumnVirtualization); Assert.Equal(44, grid.RowHeaderWidth); Assert.Equal(4, grid.Columns.Count);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(grid), text => text.Text == "Alice");
            grid.CurrentCell = new DataGridCellInfo(table.SelectedResult!.Rows[0], grid.Columns[1]);
            grid.BeginEdit(); await WindowTestVisuals.UpdateLayoutAsync(window);
            var inline = Assert.Single(WindowTestVisuals.Descendants<TextBox>(grid), input => !input.IsReadOnly);
            inline.Text = "native inline"; Assert.True(table.HasDataDraft); Assert.True(Assert.IsType<MySqlTabView>(Assert.IsType<ContentControl>(view.FindName("TabHost")).Content).CommitInput());
            Assert.Equal("native inline", table.SelectedResult.Rows[0].Cells[1].Value);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            VerifyDataActions(tableView, table, true);
            Assert.Equal(table.DraftSummary, Assert.IsType<TextBlock>(tableView.FindName("FooterStatus")).Text);
            table.RevertCommand.Execute(null);
            var scroll = Assert.Single(WindowTestVisuals.Descendants<ScrollViewer>(grid), item => item.Name == "DG_ScrollViewer");
            grid.Columns[2].Width = 500; await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(scroll.ScrollableWidth > 0); scroll.ScrollToHorizontalOffset(100); await WindowTestVisuals.UpdateLayoutAsync(window); Assert.True(scroll.HorizontalOffset > 0);
            var host = Assert.IsType<ContentControl>(view.FindName("TabHost")); var firstView = Assert.IsType<MySqlTabView>(host.Content);
            var query = model.NewQuery()!; query.Sql = "SELECT * FROM users"; await query.ExecuteAsync(query.Sql); model.ActiveTab = table; await WindowTestVisuals.UpdateLayoutAsync(window); Assert.Same(firstView, host.Content); Assert.True(scroll.HorizontalOffset > 0);
            window.Width = 650; window.Height = 660; await WindowPreviewCapture.VerifyAsync(window, $"mysql-table-narrow-{language}-{theme}");
            Assert.False(Assert.IsType<Border>(view.FindName("TreePanel")).IsVisible); Assert.True(host.ActualWidth > 500);
            VerifyDataActions(tableView, table, true);
            var draft = table.SelectedResult.Rows[0].Cells[1];
            Assert.True(await table.PrepareCellAsync(table.SelectedResult, table.SelectedResult.Rows[0], draft)); draft.Apply("pending footer");
            server.Unknown = true; await table.SaveChangesCommand.ExecuteAsync(); Assert.True(table.OutcomeUnknown);
            await WindowPreviewCapture.VerifyAsync(window, $"mysql-footer-verify-narrow-{language}-{theme}");
            VerifyDataActions(tableView, table, true);
            Assert.True(Assert.IsType<Button>(tableView.FindName("VerifyRows")).IsVisible);
            Assert.False(Assert.IsType<Button>(tableView.FindName("SaveChanges")).IsEnabled);
            model.ActiveTab = query; await WindowPreviewCapture.VerifyAsync(window, $"mysql-query-narrow-{language}-{theme}");
            VerifyDataActions(Assert.IsType<MySqlTabView>(host.Content), query, false);
            var cancel = Assert.Single(WindowTestVisuals.Descendants<Button>(view), button => ReferenceEquals(button.Command, query.CancelCommand)); Assert.Equal(Text.Get("MySqlCancelQuery"), cancel.Content); Assert.Empty(WindowTestVisuals.Descendants<CheckBox>(cancel));
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); CultureInfo.CurrentUICulture = culture; CultureInfo.DefaultThreadCurrentUICulture = defaultCulture; }
    });

    private static void VerifyDataActions(MySqlTabView view, MySqlTabViewModel model, bool pagination)
    {
        var footer = Assert.IsType<Grid>(view.FindName("DataActions"));
        Assert.True(footer.IsVisible); Assert.Equal(32, footer.ActualHeight);
        Assert.Equal(pagination, Assert.IsType<StackPanel>(view.FindName("PageButtons")).IsVisible);
        foreach (var button in WindowTestVisuals.Descendants<Button>(footer).Where(button => button.IsVisible))
        {
            var origin = button.TranslatePoint(new Point(), footer);
            Assert.InRange(origin.X, 0, footer.ActualWidth - button.ActualWidth + 0.1);
            Assert.Equal(0, origin.Y); Assert.Equal(32, button.ActualHeight);
        }
        Assert.Equal(model.EditHint, System.Windows.Automation.AutomationProperties.GetHelpText(footer));
        foreach (var (name, key) in new[] { ("PreviousPage", "MySqlPrevious"), ("NextPage", "MySqlNext") })
        {
            var button = Assert.IsType<Button>(view.FindName(name));
            Assert.Equal(Text.Get(key), button.ToolTip); Assert.Equal(Text.Get(key), System.Windows.Automation.AutomationProperties.GetName(button));
            if (pagination)
            {
                var drawing = Assert.IsType<DrawingGroup>(VisualTreeHelper.GetDrawing(Assert.Single(WindowTestVisuals.Descendants<IconView>(button))));
                Assert.False(drawing.Bounds.IsEmpty); Assert.True(drawing.Bounds.Width > 0);
            }
        }
    }

    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task FieldWindow_UsesSharedResizableWindowAndEditableCode(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace(); var old = CultureInfo.CurrentUICulture; var oldDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture); Text.SetApplicationCulture(Text.Culture); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var cell = new MySqlGridViewModel(new TestMySql().Result()).Rows[0].Cells[2]; var model = new MySqlFieldEditorViewModel(cell, "app.users.profile · id=1", true);
        var window = new MySqlFieldDialog(model);
        try
        {
            WindowPreviewCapture.Show(window); await WindowPreviewCapture.VerifyAsync(window, $"mysql-field-{language}-{theme}", 60);
            Assert.Equal(ResizeMode.CanResize, window.ResizeMode); Assert.False(Assert.IsType<CodeEditor>(window.FindName("FieldInput")).IsReadOnly);
            Assert.Single(WindowTestVisuals.Descendants<CheckBox>(window)); Assert.True(model.CanApply);
            window.Width = 560; window.Height = 500; await WindowPreviewCapture.VerifyAsync(window, $"mysql-field-narrow-{language}-{theme}", 60);
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); CultureInfo.CurrentUICulture = old; CultureInfo.DefaultThreadCurrentUICulture = oldDefault; }
    });
    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        public IReadOnlyList<InstanceSummary> GetInstances() => [];
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestMySql.Connection);
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
