using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实临时链接窗体只在可见按钮点击后生成和复制。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageTemporaryLinkWindowTests(WpfTestFixture fixture)
{
    /// <summary>加载无副作用，实际有效期选择、生成和复制按钮分别调用对应边界。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task TemporaryLinkDialog_OnlyExplicitActionsGenerateAndCopy(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        int calls = 0, copies = 0;
        TimeSpan received = default;
        using var model = new ObjectStorageTemporaryLinkDialogViewModel("private/config.json", duration =>
        {
            calls++;
            received = duration;
            return Task.FromResult("https://example.test/private?signature=generated");
        }, _ => { copies++; return true; });
        var owner = ObjectStorageDialogTestVisuals.CreateOwner();
        var dialog = new ObjectStorageTemporaryLinkWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                ObjectStorageDialogTestVisuals.AssertCommonFrame(dialog, owner, 670, 380);
                Assert.Equal(0, calls);
                Assert.Equal(0, copies);
                var generate = Assert.IsType<Button>(dialog.FindName("GenerateButton"));
                Assert.False(generate.IsDefault);
                var duration = Assert.IsType<ComboBox>(dialog.FindName("LifetimeSelector"));
                duration.SelectedIndex = 1;
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                WindowTestVisuals.Invoke(generate);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.Equal(1, calls);
                Assert.Equal(TimeSpan.FromHours(1), received);
                Assert.Equal(0, copies);
                var preview = Assert.IsType<TextBox>(dialog.FindName("LinkPreview"));
                Assert.True(preview.IsReadOnly);
                Assert.Equal(model.Link, preview.Text);
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(dialog), text =>
                    text.Text == "获得链接的人可在有效期内访问此对象，请谨慎分享。");
                await WindowPreviewCapture.VerifyAsync(dialog, $"object-storage-temporary-link-{theme}");
                WindowTestVisuals.Invoke(ObjectStorageDialogTestVisuals.CommandButton(dialog, model.CopyCommand));
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.Equal(1, copies);
                Assert.Equal("已复制链接。", model.CopyStatus);
                WindowTestVisuals.Invoke(ObjectStorageDialogTestVisuals.CommandButton(dialog, model.CancelCommand));
                await WpfTestFixture.DrainDispatcherAsync();
            });
            Assert.Equal<bool?>(false, result);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            owner.Close();
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>标题栏关闭时忽略迟到生成结果，不能在模态结束后自动复制。</summary>
    [Fact]
    public Task TemporaryLinkDialog_CaptionCloseIgnoresLateUrl() => fixture.RunAsync(async () =>
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int copies = 0;
        using var model = new ObjectStorageTemporaryLinkDialogViewModel("private.json", _ => completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
            _ => { copies++; return true; });
        var owner = ObjectStorageDialogTestVisuals.CreateOwner();
        var dialog = new ObjectStorageTemporaryLinkWindow(model) { Opacity = 0 };
        Task? generating = null;
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                generating = model.GenerateCommand.ExecuteAsync();
                Assert.True(model.IsGenerating);
                WindowTestVisuals.Invoke(ObjectStorageDialogTestVisuals.CaptionClose(dialog));
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.False(dialog.IsVisible);
            });
            completion.SetResult("https://example.test/late-private-url");
            Assert.NotNull(generating);
            await generating;
            Assert.Equal<bool?>(false, result);
            Assert.Empty(model.Link);
            Assert.Equal(0, copies);
            Assert.False(model.CopyCommand.CanExecute(null));
        }
        finally
        {
            completion.TrySetResult("https://example.test/cleanup");
            if (dialog.IsVisible) dialog.Close();
            owner.Close();
        }
    });
}
