using System.Globalization;
using System.Windows.Controls.Primitives;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceLayoutTests(WpfTestFixture fixture)
{
    /// <summary>直接挂载控件检验页签缓存、原生滚动和窄窗口，不创建或显示窗口。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Workspace_UsesNativeGridScrollAndCachedTabsInBothLanguages(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        try
        {
            var server = new TestMySql(); var configurations = new Configurations();
            await using var model = new MySqlWorkspaceViewModel(new(new("mysql-test", "MySQL", "MySQL", "database", [], TypeId: MySqlIds.Type, ProviderId: MySqlIds.Direct)), configurations, server, configurations, new TestMySqlDialogs(), "app");
            await model.InitializeAsync(); await model.OpenObjectAsync(new(MySqlObjectKind.Table, "users", "app")); var table = model.ActiveTab!;
            var view = new MySqlWorkspaceView { DataContext = model }; var root = new Border { Child = view };
            root.Resources.MergedDictionaries.Add(Application.Current.Resources); await Layout(1200);
            var host = (ContentControl)view.FindName("TabHost"); var firstView = Assert.IsType<MySqlTabView>(host.Content);
            Assert.Equal(58, ((DatabaseObjectHeader)firstView.FindName("ObjectContext")).ActualHeight);
            Assert.Same(model.ReturnToScriptCommand, ((Button)((DatabaseObjectHeader)firstView.FindName("ObjectContext")).FindName("ReturnToScript")).Command);
            var grid = Assert.Single(WindowTestVisuals.Descendants<DataGrid>(firstView), item => item.Name == "ResultGrid");
            Assert.Equal(36, grid.ColumnHeaderHeight); Assert.Equal(42, grid.RowHeight); Assert.Equal(44, grid.RowHeaderWidth);
            var scroll = Assert.Single(WindowTestVisuals.Descendants<ScrollViewer>(grid), item => item.Name == "DG_ScrollViewer");
            grid.Columns[2].Width = 900; await Layout(1200); scroll.ScrollToHorizontalOffset(100); await Layout(1200); Assert.True(scroll.HorizontalOffset > 0);
            var query = model.NewQuery()!; query.Sql = "SELECT * FROM users"; await query.ExecuteAsync(query.Sql);
            model.ActiveTab = table; await Layout(1200); Assert.Same(firstView, host.Content); Assert.True(scroll.HorizontalOffset > 0);
            await Layout(650); Assert.Equal(Visibility.Collapsed, ((Border)view.FindName("TreePanel")).Visibility); Assert.True(host.ActualWidth > 500);
            var result = table.SelectedResult!; var cell = result.Rows[0].Cells[1]; Assert.True(await table.PrepareCellAsync(result, result.Rows[0], cell)); cell.Apply("pending footer");
            server.Unknown = true; await table.SaveChangesCommand.ExecuteAsync(); await Layout(650);
            Assert.True(table.OutcomeUnknown); Assert.Equal(Visibility.Visible, ((Button)firstView.FindName("VerifyRows")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((Button)firstView.FindName("SaveChanges")).Visibility);
            Assert.Equal(table.ObjectFooterText, ((TextBlock)firstView.FindName("FooterStatus")).Text);
            async Task Layout(double width)
            {
                root.Measure(new(width, 800)); root.Arrange(new(0, 0, width, 800)); root.UpdateLayout();
                await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
                Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            }
        }
        finally { themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });

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
