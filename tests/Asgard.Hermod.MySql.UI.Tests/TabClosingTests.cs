using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>真实 WPF 选中项回写参与关闭流程，防止模型测试掩盖空白内容回归。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class TabClosingTests(WpfTestFixture fixture)
{
    /// <summary>关闭非活动、活动和最后一个页签，保留相邻视图、草稿和对象树。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task ClosingTabs_KeepsSelectedContentAndShowsEmptyGuide(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace(); var previous = CultureInfo.CurrentUICulture;
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var server = new TestMySql(); var configurations = new Configurations();
        await using var model = new MySqlWorkspaceViewModel(new(new("mysql", "Database", "Database", "database", [], TypeId: MySqlIds.Type)),
            configurations, server, configurations, new TestMySqlDialogs());
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var first = model.ActiveTab!; var second = model.NewQuery()!; var third = model.NewQuery()!;
        var view = new MySqlWorkspaceView { DataContext = model }; var window = new HermodWindow { Content = view, Width = 1120, Height = 780 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var host = Assert.IsType<ContentControl>(view.FindName("TabHost")); var tabs = Assert.IsType<WorkspaceTabStrip>(view.FindName("ObjectTabs"));
            var thirdView = host.Content;
            await model.CloseTabCommand.ExecuteAsync(first); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(third, model.ActiveTab); Assert.Same(thirdView, host.Content);
            model.ActiveTab = second; await WindowTestVisuals.UpdateLayoutAsync(window);
            await model.CloseTabCommand.ExecuteAsync(second); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(third, model.ActiveTab); Assert.Same(third, tabs.SelectedItem); Assert.Same(thirdView, host.Content);
            third.Sql = "unsaved query";
            await model.CloseTabCommand.ExecuteAsync(third); Assert.Same(third, model.ActiveTab); Assert.Single(model.Tabs);
            third.Sql = "";
            var tree = model.Tree; int opens = server.Opens;
            await model.CloseTabCommand.ExecuteAsync(third); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Null(model.ActiveTab); Assert.Null(host.Content); Assert.False(tabs.IsVisible);
            Assert.True(Assert.IsType<StackPanel>(view.FindName("EmptyTabs")).IsVisible); Assert.Same(tree, model.Tree); Assert.True(model.IsConnected);
            await WindowPreviewCapture.VerifyAsync(window, $"mysql-no-tabs-{language}-{theme}");
            model.NewQueryCommand.Execute(null); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(tabs.IsVisible); Assert.Same(model.ActiveTab, Assert.IsType<MySqlTabView>(host.Content).DataContext); Assert.Equal(opens, server.Opens);
            Assert.Empty(trace.Messages);
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); CultureInfo.CurrentUICulture = previous; }
    });
    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        public IReadOnlyList<InstanceSummary> GetInstances() => [];
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestMySql.Connection);
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
