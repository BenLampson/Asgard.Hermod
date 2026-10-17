using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>新增弹窗的真实 WPF 模板涵盖双语、主题、类型切换及失败后的输入保留。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class RedisCreateDialogTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task CreateDialog_UsesSharedEditorsAndKeepsInputAfterConflict(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace(); var previous = CultureInfo.CurrentUICulture; var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var session = new RedisTestSession { SaveError = new RedisException("RedisKeyAlreadyExists", "REDIS_KEY_EXISTS") };
        using var model = new RedisCreateViewModel(session, 5, "cache:") { Key = "cache:homepage", EditorText = "{\n  \"version\": 12\n}", ExpiryMode = "seconds", ExpirySeconds = "3600" };
        var window = new RedisCreateDialog(model);
        try
        {
            WindowPreviewCapture.Show(window); model.Format = "json";
            await WindowPreviewCapture.VerifyAsync(window, $"redis-create-string-{language}-{theme}", 60);
            var editor = Assert.IsType<CodeEditor>(window.FindName("StringEditor")); Assert.False(editor.IsReadOnly);
            await model.SubmitCommand.ExecuteAsync(); await WindowPreviewCapture.VerifyAsync(window, $"redis-create-conflict-{language}-{theme}", 60);
            Assert.True(window.IsVisible); Assert.True(model.HasConflict); Assert.Equal("cache:homepage", model.Key); Assert.Equal("3600", model.ExpirySeconds);
            Assert.True(Assert.IsType<Button>(window.FindName("InspectButton")).IsVisible);
            foreach (string type in new[] { "hash", "list", "set", "zset", "stream" })
            {
                model.SelectedType = type; model.Rows[0].Name = "field"; model.Rows[0].Value = "initial value"; model.Rows[0].Score = "1.5";
                await WindowPreviewCapture.VerifyAsync(window, $"redis-create-{type}-{language}-{theme}", 60);
                var grid = Assert.IsType<DataGrid>(window.FindName("CollectionGrid")); Assert.True(grid.IsVisible); Assert.False(editor.IsVisible);
                Assert.Equal(model.HasName, grid.Columns[0].Visibility == Visibility.Visible); Assert.Equal(model.HasScore, grid.Columns[2].Visibility == Visibility.Visible);
            }
            model.SelectedType = "string"; window.Width = 560; window.Height = 580;
            await WindowPreviewCapture.VerifyAsync(window, $"redis-create-narrow-{language}-{theme}", 60);
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault; }
    });
}
