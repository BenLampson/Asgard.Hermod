using Asgard.Hermod.UI.Views.Dialogs;
using System.Windows.Media.Imaging;

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

    /// <summary>长签名链接不会挤出复制按钮，实际渲染中的选区和白字在两种主题下清晰可见。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task TemporaryLinkDialog_LongLinkSelectionIsVisibleAndCopiesFullUrl(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        const string key = "bridge/production/qualifications/12345678-1234-1234-1234-123456789abc/long-object-name.png";
        string link = "https://example.test/" + key + "?x-oss-signature-version=OSS4-HMAC-SHA256&signature=" + new string('a', 256);
        string? copied = null;
        using var model = new ObjectStorageTemporaryLinkDialogViewModel(key, _ => Task.FromResult(link),
            value => { copied = value; return true; });
        var owner = ObjectStorageDialogTestVisuals.CreateOwner();
        var dialog = new ObjectStorageTemporaryLinkWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                WindowTestVisuals.Invoke(Assert.IsType<Button>(dialog.FindName("GenerateButton")));
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                var preview = Assert.IsType<TextBox>(dialog.FindName("LinkPreview"));
                var copy = Assert.IsType<Button>(dialog.FindName("CopyButton"));
                var scroll = Assert.IsType<ScrollViewer>(dialog.FindName("DialogScrollViewer"));
                Assert.Equal(Visibility.Collapsed, scroll.ComputedVerticalScrollBarVisibility);
                Point inputOrigin = preview.TranslatePoint(new Point(), dialog);
                Point copyOrigin = copy.TranslatePoint(new Point(), dialog);
                Assert.True(inputOrigin.X + preview.ActualWidth + 8 <= copyOrigin.X + 1);
                Assert.Equal(preview.ActualHeight, copy.ActualHeight);

                preview.Focus();
                preview.Select(25, 30);
                preview.ScrollToHorizontalOffset(0);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.Equal(link.Substring(25, 30), preview.SelectedText);
                await WindowPreviewCapture.VerifyAsync(dialog, $"object-storage-temporary-link-partial-selection-{theme}");
                AssertRenderedSelection(preview);

                preview.SelectAll();
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.Equal(link, preview.SelectedText);
                await WindowPreviewCapture.VerifyAsync(dialog, $"object-storage-temporary-link-full-selection-{theme}");
                AssertRenderedSelection(preview);

                // 焦点移到复制按钮后仍能辨认原选区，复制回调必须收到完整签名 URL。
                copy.Focus();
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                AssertRenderedSelection(preview);
                WindowTestVisuals.Invoke(copy);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.Equal(link, copied);
                Assert.Equal(Visibility.Collapsed, scroll.ComputedVerticalScrollBarVisibility);
                WindowTestVisuals.Invoke(ObjectStorageDialogTestVisuals.CommandButton(dialog, model.CancelCommand));
            });
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            owner.Close();
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>检查正文区域的真实像素，避免只验证样式属性却漏掉 WPF 选区绘制异常。</summary>
    private static void AssertRenderedSelection(TextBox preview)
    {
        int width = (int)Math.Ceiling(preview.ActualWidth);
        int height = (int)Math.Ceiling(preview.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(preview);
        byte[] pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        Color selection = Assert.IsType<SolidColorBrush>(preview.SelectionBrush).Color;
        Color text = Assert.IsType<SolidColorBrush>(preview.SelectionTextBrush).Color;
        int selectedPixels = 0, textPixels = 0;
        for (int y = 8; y < height - 8; y++)
        {
            for (int x = 12; x < width - 12; x++)
            {
                int offset = (y * width + x) * 4;
                if (pixels[offset] == selection.B && pixels[offset + 1] == selection.G && pixels[offset + 2] == selection.R)
                    selectedPixels++;
                if (pixels[offset] == text.B && pixels[offset + 1] == text.G && pixels[offset + 2] == text.R)
                    textPixels++;
            }
        }
        Assert.True(selectedPixels > 100, $"选区背景没有清晰渲染：{selectedPixels} 像素。");
        Assert.True(textPixels > 10, $"选中文本没有清晰渲染：{textPixels} 像素。");
    }

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
