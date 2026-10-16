using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>关系类型码只在提示中本地化，整行使用同一个提示且不改变数据库原始元数据。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectTreeHintTests(WpfTestFixture fixture)
{
    /// <summary>覆盖全部已支持关系类型、普通元数据和空说明，以及英文回退。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    [InlineData("fr-FR", UiTheme.Light)]
    public Task RowHint_UsesReadableTypeAndPreservesOriginalMetadata(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        try
        {
            PostgreSqlObject[] objects = [
                new(PostgreSqlObjectKind.Table, "qishu_drawing", Detail: "r"),
                new(PostgreSqlObjectKind.Table, "partitioned", Detail: "p"),
                new(PostgreSqlObjectKind.Table, "foreign", Detail: "f"),
                new(PostgreSqlObjectKind.View, "view", Detail: "v"),
                new(PostgreSqlObjectKind.View, "materialized", Detail: "m"),
                new(PostgreSqlObjectKind.Column, "raw_column", Detail: "r"),
                new(PostgreSqlObjectKind.Schema, "public")];
            string[] details = language == "zh-Hans"
                ? ["普通表", "分区表", "外部表", "视图", "物化视图", "r", ""]
                : ["Ordinary table", "Partitioned table", "Foreign table", "View", "Materialized view", "r", ""];
            var server = new TestPostgreSql { MetadataOverride = _ => objects };
            await using var session = await server.OpenAsync(TestPostgreSql.Connection);
            using var tree = new PostgreSqlObjectTree(session); await tree.LoadAsync(tree.Root);
            // 使用真实模块行模板，确认文字本身不会再盖过整行提示。
            var resources = new PostgreSqlWorkspaceView().Resources;
            var view = new WorkspaceTreeView
            {
                Resources = resources,
                ItemsSource = tree.Root.Children,
                ItemContainerStyle = (Style)resources["ObjectTreeItem"]
            };
            var root = new Border { Child = view };
            root.Resources.MergedDictionaries.Add(Application.Current.Resources);
            root.Measure(new(232, 600)); root.Arrange(new(0, 0, 232, 600)); root.UpdateLayout();
            await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
            for (int index = 0; index < objects.Length; index++)
            {
                var node = tree.Root.Children[index];
                string expected = details[index].Length == 0 ? objects[index].Name : $"{objects[index].Name}\n{details[index]}";
                Assert.Equal(expected, node.Hint); Assert.Equal(objects[index].Detail, node.Detail);
                var row = Assert.IsAssignableFrom<TreeViewItem>(view.ItemContainerGenerator.ContainerFromItem(node));
                Assert.Equal(expected, row.ToolTip);
                var label = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(row), text => text.Text == node.DisplayLabel);
                Assert.Null(label.ToolTip);
            }
        }
        finally { Text.SetApplicationCulture(null); themes.SetTheme(UiTheme.Light); }
    });
}
