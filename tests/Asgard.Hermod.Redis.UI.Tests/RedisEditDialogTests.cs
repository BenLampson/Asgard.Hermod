namespace Asgard.Hermod.Redis.Tests;

/// <summary>实际弹窗编辑，工作区保持只读；关闭确认及保存失败不会丢失草稿。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class RedisEditDialogTests(WpfTestFixture fixture)
{
    [Fact]
    public Task SaveButton_CommitsPopupBufferAndReturnsToReadonlyPreview() => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace(); var session = new RedisTestSession();
        using var tab = new RedisKeyTabViewModel(session, 0, RedisKeyId.FromText("popup:save"), new RedisTestDialogs());
        await tab.ReadAsync(); await tab.EditCommand.ExecuteAsync(); var window = new RedisEditDialog(tab);
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var view = Assert.IsType<RedisKeyContentView>(window.FindName("Editor"));
            var code = Assert.IsType<CodeEditor>(view.FindName("StringEditor")); var input = Assert.IsType<TextBox>(code.FindName("Input"));
            input.Text = "saved through popup"; await WindowTestVisuals.UpdateLayoutAsync(window);
            var save = Assert.IsType<Button>(window.FindName("SaveButton")); Assert.True(save.IsEnabled); save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WpfTestFixture.DrainDispatcherAsync(); Assert.False(window.IsVisible); Assert.Equal(1, session.Saves); Assert.False(tab.IsDirty); Assert.False(tab.IsEditing);
            Assert.Equal("saved through popup", System.Text.Encoding.UTF8.GetString(Assert.Single(session.Snapshot.Entries).Value));
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task Editor_LeavesPreviewReadonlyAndHonorsDirtyCloseChoices() => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace(); var session = new RedisTestSession(); var dialogs = new RedisTestDialogs();
        using var tab = new RedisKeyTabViewModel(session, 0, RedisKeyId.FromText("cache:homepage"), dialogs);
        await tab.ReadAsync(); var preview = new RedisKeyContentView { DataContext = tab };
        var owner = new HermodWindow { Content = preview, Width = 800, Height = 650 };
        WindowPreviewCapture.Show(owner); await tab.EditCommand.ExecuteAsync(); var window = new RedisEditDialog(tab) { Owner = owner };
        try
        {
            WindowPreviewCapture.Show(window); await WindowPreviewCapture.VerifyAsync(window, "redis-edit-modal", 60);
            Assert.True(Assert.IsType<CodeEditor>(preview.FindName("StringEditor")).IsReadOnly);
            var editor = Assert.IsType<RedisKeyContentView>(window.FindName("Editor")); Assert.False(Assert.IsType<CodeEditor>(editor.FindName("StringEditor")).IsReadOnly);
            tab.EditorText = "kept draft"; session.SaveError = new RedisException("RedisConflict", "REDIS_CONFLICT");
            Assert.False(await tab.SaveAsync()); Assert.True(window.IsVisible); Assert.Equal("kept draft", tab.EditorText);
            window.Close(); await WpfTestFixture.DrainDispatcherAsync(); Assert.True(window.IsVisible); Assert.True(tab.IsDirty);
            dialogs.Choice = RedisCloseChoice.Discard; window.Close(); await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(window.IsVisible); Assert.False(tab.IsDirty); Assert.False(tab.IsEditing); Assert.Equal("value", tab.EditorText);
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { dialogs.Choice = RedisCloseChoice.Discard; window.Close(); await WpfTestFixture.DrainDispatcherAsync(); owner.Close(); }
    });
}
