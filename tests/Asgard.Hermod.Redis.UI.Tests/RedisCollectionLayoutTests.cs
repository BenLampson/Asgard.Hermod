namespace Asgard.Hermod.Redis.Tests;

/// <summary>实际模板覆盖所有协议类型，尤其只读记录列和元数据不能产生反向写入绑定。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class RedisCollectionLayoutTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("hash")]
    [InlineData("list")]
    [InlineData("set")]
    [InlineData("zset")]
    [InlineData("stream")]
    public Task Collection_MetadataAndEditorRenderWithoutBindingErrors(string type) => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace();
        var session = new RedisTestSession { Snapshot = new(type, 1, -1, [new("field"u8.ToArray(), "value"u8.ToArray(), "1.5", "123-0")], true) };
        var dialogs = new RedisTestDialogs(); using var tab = new RedisKeyTabViewModel(session, 0, RedisKeyId.FromText("test:" + type), dialogs);
        var view = new RedisKeyContentView { DataContext = tab }; var window = new HermodWindow { Width = 760, Height = 600, Content = view };
        try
        {
            WindowPreviewCapture.Show(window); await tab.ReadAsync();
            await WindowPreviewCapture.VerifyAsync(window, "redis-collection-" + type);
            tab.Section = "metadata"; await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Contains(WindowTestVisuals.Descendants<TextBox>(view), box => box.IsVisible && box.IsReadOnly && box.Text == tab.FullName);
            tab.Section = "value"; await WindowTestVisuals.UpdateLayoutAsync(window);
            var grid = Assert.IsType<DataGrid>(view.FindName("CollectionGrid")); Assert.True(grid.IsVisible); Assert.Single(grid.Items.Cast<object>());
            if (type != "stream")
            {
                await tab.EditCommand.ExecuteAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.False(grid.IsReadOnly); grid.SelectedIndex = 0;
                grid.CurrentCell = new(grid.Items[0], grid.Columns.First(column => column.Visibility == Visibility.Visible && !column.IsReadOnly));
                Assert.True(grid.BeginEdit()); grid.CommitEdit(); await WindowTestVisuals.UpdateLayoutAsync(window);
            }
            else Assert.True(grid.IsReadOnly);
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { window.Close(); }
    });
}
