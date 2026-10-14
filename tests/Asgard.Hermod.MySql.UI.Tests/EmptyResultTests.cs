using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>空结果保留列定义和返回上一页能力，不暴露不可用的新增或编辑入口。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class EmptyResultTests(WpfTestFixture fixture)
{
    /// <summary>首个空页、只读行、可编辑行和最后一页边界随真实读取结果切换。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    [InlineData("fr-FR", UiTheme.Light)]
    public Task EmptyPagesAndReadOnlyRows_ShowOnlyApplicableActions(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var server = new TestMySql(); var populated = server.Result(); server.ResultOverride = populated with { Rows = [] };
        await using var tab = new MySqlTabViewModel("identity_provider_config", "dev_idp", MySqlObjectKind.Table, server, TestMySql.Connection, new TestMySqlDialogs());
        var view = new MySqlTabView { DataContext = tab }; var window = new HermodWindow { Width = 650, Height = 660, Content = view };
        try
        {
            WindowPreviewCapture.Show(window); await tab.ReadAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(tab.HasResult); Assert.False(tab.HasDataRows); VerifyEmptyResult(view, language);
            Assert.False(Assert.IsType<StackPanel>(view.FindName("PageButtons")).IsVisible);
            Assert.False(Assert.IsType<StackPanel>(view.FindName("EditActions")).IsVisible);
            await WindowPreviewCapture.VerifyAsync(window, $"mysql-empty-result-{language}-{theme}");
            server.ResultOverride = populated with { EditableTable = null }; await tab.ReadAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(tab.HasDataRows); Assert.False(tab.CanEditRows);
            Assert.False(Assert.IsType<StackPanel>(view.FindName("PageButtons")).IsVisible);
            Assert.False(Assert.IsType<StackPanel>(view.FindName("EditActions")).IsVisible);
            server.ResultOverride = populated with { Rows = Enumerable.Repeat(populated.Rows[0], 200).ToArray(), HasMore = true }; await tab.ReadAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(tab.CanEditRows); Assert.True(Assert.IsType<StackPanel>(view.FindName("EditActions")).IsVisible); Assert.True(tab.NextCommand.CanExecute(null));
            server.ResultOverride = populated with { Rows = [] }; await tab.NextCommand.ExecuteAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(200, tab.Offset); VerifyEmptyResult(view, language);
            Assert.True(Assert.IsType<StackPanel>(view.FindName("PageButtons")).IsVisible);
            Assert.True(Assert.IsType<Button>(view.FindName("PreviousPage")).IsEnabled); Assert.False(Assert.IsType<Button>(view.FindName("NextPage")).IsEnabled);
            Assert.False(Assert.IsType<StackPanel>(view.FindName("EditActions")).IsVisible);
            server.ResultOverride = populated; await tab.PreviousCommand.ExecuteAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(0, tab.Offset); Assert.True(tab.HasDataRows); Assert.True(tab.CanEditRows);
            Assert.True(Assert.IsType<StackPanel>(view.FindName("EditActions")).IsVisible); Assert.Equal(0, server.Writes);
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });

    private static void VerifyEmptyResult(MySqlTabView view, string language)
    {
        var result = Assert.IsType<MySqlResultView>(Assert.IsType<ContentControl>(view.FindName("GridHost")).Content);
        var grid = Assert.IsType<DataGrid>(result.FindName("ResultGrid"));
        Assert.Empty(grid.Items); Assert.Equal(4, grid.Columns.Count); Assert.False(grid.CanUserAddRows);
        var message = Assert.IsType<TextBlock>(result.FindName("EmptyResult")); Assert.True(message.IsVisible);
        Assert.Equal(language == "zh-Hans" ? "暂无数据。" : "No rows to display.", message.Text);
        Assert.DoesNotContain(WindowTestVisuals.Descendants<Button>(view), button => button.IsVisible && ReferenceEquals(button.Command, ((MySqlTabViewModel)view.DataContext).SaveChangesCommand));
    }
}
