using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>使用真实模态消息循环验证对象上传、目录名称和删除确认的控件与主题。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageDialogWindowTests(WpfTestFixture fixture)
{
    /// <summary>上传队列移除和策略绑定由实际控件触发，确认只返回剩余文件。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task UploadDialog_UsesSharedFrameAndReturnsControlledQueue(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        using var model = new ObjectStorageUploadViewModel(Bucket(), "releases/", (_, _) =>
            Task.FromResult<IReadOnlyList<ObjectStorageUploadItem>>(
                [new("C:/selected/a.json", "releases/a.json", 100), new("C:/selected/b.txt", "releases/b.txt", 20)]));
        await model.AddPathsAsync(["C:/selected"]);
        var owner = ObjectStorageDialogTestVisuals.CreateOwner();
        var dialog = new ObjectStorageUploadWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                ObjectStorageDialogTestVisuals.AssertCommonFrame(dialog, owner, 600, 620);
                var drop = Assert.IsType<Border>(dialog.FindName("DropArea"));
                Assert.Equal(142d, drop.ActualHeight);
                Assert.Equal(((SolidColorBrush)Application.Current.FindResource("AppBackgroundBrush")).Color,
                    Assert.IsType<SolidColorBrush>(drop.Background).Color);
                await WindowPreviewCapture.VerifyAsync(dialog, $"object-storage-upload-{theme}");
                var choices = Assert.IsType<ComboBox>(dialog.FindName("OverwriteSelector"));
                Assert.Equal(ObjectStorageOverwritePolicy.Ask, choices.SelectedValue);
                choices.SelectedValue = ObjectStorageOverwritePolicy.Skip;
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                var remove = Assert.Single(WindowTestVisuals.Descendants<Button>(dialog), button =>
                    ReferenceEquals(button.Command, model.RemoveCommand) && ReferenceEquals(button.CommandParameter, model.Items[0]));
                WindowTestVisuals.Invoke(remove);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.Single(model.Items);
                WindowTestVisuals.Invoke(Assert.IsType<Button>(dialog.FindName("UploadButton")));
                await WpfTestFixture.DrainDispatcherAsync();
            });
            Assert.Equal<bool?>(true, result);
            Assert.NotNull(model.Result);
            Assert.Equal("releases/b.txt", Assert.Single(model.Result.Items).Key);
            Assert.Equal(ObjectStorageOverwritePolicy.Skip, model.Result.OverwritePolicy);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            owner.Close();
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>非法目录名称留在窗体内就地提示，通过修正后的实际表单才关闭。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task FolderDialog_ValidatesInlineAndReturnsCorrectedName(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        using var model = new ObjectStorageFolderDialogViewModel(Bucket(), "releases/");
        var owner = ObjectStorageDialogTestVisuals.CreateOwner();
        var dialog = new ObjectStorageFolderWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                ObjectStorageDialogTestVisuals.AssertCommonFrame(dialog, owner, 520, 340);
                await WindowPreviewCapture.VerifyAsync(dialog, $"object-storage-folder-{theme}");
                var input = Assert.IsType<TextBox>(dialog.FindName("NameInput"));
                var confirm = Assert.IsType<Button>(dialog.FindName("CreateButton"));
                input.Text = "bad/path";
                WindowTestVisuals.Invoke(confirm);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.True(dialog.IsVisible);
                Assert.NotEmpty(model.Error);
                Assert.True(FormField.GetHasError(input));
                input.Text = "  documents  ";
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.Empty(model.Error);
                WindowTestVisuals.Invoke(confirm);
                await WpfTestFixture.DrainDispatcherAsync();
            });
            Assert.Equal<bool?>(true, result);
            Assert.Equal("documents", model.Result);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
        }
        finally { if (dialog.IsVisible) dialog.Close(); owner.Close(); themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>完整删除清单可滚动查看，危险按钮没有默认确认，并使用当前主题警示色。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task DeleteDialog_ContainsAllExactKeysAndOnlyExplicitClickConfirms(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        string[] keys = ["releases/config.json", "releases/nested/manifest.json", "releases/" + new string('x', 200) + ".txt"];
        using var model = ObjectStorageConfirmationDialogViewModel.ForDelete(Bucket(), "releases/", new(keys));
        var owner = ObjectStorageDialogTestVisuals.CreateOwner();
        var dialog = new ObjectStorageConfirmationWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                ObjectStorageDialogTestVisuals.AssertCommonFrame(dialog, owner, 620, 420);
                var list = Assert.IsType<ItemsControl>(dialog.FindName("ObjectKeys"));
                Assert.Equal(3, list.Items.Count);
                foreach (string key in keys)
                    Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(list), text => text.Text == key);
                var confirm = ObjectStorageDialogTestVisuals.CommandButton(dialog, model.ConfirmCommand);
                Assert.False(confirm.IsDefault);
                Assert.False(model.Result);
                Assert.Equal(((SolidColorBrush)Application.Current.FindResource("DangerBrush")).Color,
                    Assert.IsType<SolidColorBrush>(confirm.Background).Color);
                await WindowPreviewCapture.VerifyAsync(dialog, $"object-storage-delete-{theme}");
                WindowTestVisuals.Invoke(confirm);
                await WpfTestFixture.DrainDispatcherAsync();
            });
            Assert.Equal<bool?>(true, result);
            Assert.True(model.Result);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            owner.Close();
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>对话框测试不会连接真实对象服务。</summary>
    private static ObjectStorageBucket Bucket() => new("assets-dev", "cn-hangzhou");
}
