using System.Globalization;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证双语的真实模态删除流程、键盘确认与进度切换。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageDeleteViewTests(WpfTestFixture fixture)
{
    /// <summary>完整显示选中范围；确认后只显示进度和结果，后代不会变成列表行。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light, 620, 420, false)]
    [InlineData("zh-Hans", UiTheme.Dark, 460, 340, true)]
    public Task DeleteDialog_RendersBothRealProgressStages(string language, UiTheme theme, double width, double height, bool stopWithCaption) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var token = lifetime.Token;
        var collected = new TaskCompletionSource<ObjectStorageDeletePlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IProgress<int>? collectProgress = null;
        IProgress<int>? deleteProgress = null;
        var entries = Enumerable.Range(1, 31).Select(index => new ObjectStorageEntry($"releases/应用 {index}/", true)).ToArray();
        using var model = new ObjectStorageDeleteDialogViewModel(new("assets-dev", "region"), "releases/", entries,
            (progress, request) => { collectProgress = progress; return collected.Task.WaitAsync(request); },
            (_, progress, request) => { deleteProgress = progress; deleting.SetResult(); return deleted.Task.WaitAsync(request); }, token);
        var dialog = new ObjectStorageDeleteWindow(model) { Opacity = 0, Width = width, Height = height };
        var owner = ObjectStorageDialogTestVisuals.CreateOwner();
        try
        {
            owner.Show();
            dialog.Owner = owner;
            await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                try
                {
                    var list = Assert.IsType<ItemsControl>(dialog.FindName("SelectedKeys"));
                    var confirm = Assert.IsType<Button>(dialog.FindName("ConfirmButton"));
                    var progress = Assert.IsType<ProgressBar>(dialog.FindName("DeleteProgress"));
                    var dismiss = Assert.IsType<Button>(dialog.FindName("DismissButton"));
                    await WindowPreviewCapture.VerifyAsync(dialog, $"object-delete-confirm-{language}");
                    Assert.Equal(Text.Get("DeleteObjects2"), dialog.Title);
                    Assert.Equal(Text.Format("ObjectStorageDeleteConfirmSelection", entries.Length), model.Heading);
                    Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(dialog), text => text.Text == Text.Get("ObjectStorageDeleteRecursiveWarning"));
                    Assert.Equal(entries.Select(entry => entry.Key), list.Items.Cast<string>());
                    Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(entries.Length - 1));
                    Assert.False(progress.IsVisible);
                    Assert.False(confirm.IsDefault);
                    Assert.Null(collectProgress);
                    Assert.Equal(((SolidColorBrush)Application.Current.FindResource("DangerBrush")).Color,
                        Assert.IsType<SolidColorBrush>(confirm.Background).Color);
                    Assert.Single(WindowTestVisuals.Descendants<ScrollViewer>(list)).ScrollToEnd();
                    await WindowTestVisuals.UpdateLayoutAsync(dialog);
                    Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(entries.Length - 1));

                    SecretInputTests.ToggleWithKeyboard(confirm);
                    Assert.NotNull(collectProgress);
                    collectProgress.Report(2500);
                    await WindowPreviewCapture.VerifyAsync(dialog, $"object-delete-collect-{language}");
                    Assert.True(progress.IsVisible);
                    Assert.True(progress.IsIndeterminate);
                    Assert.Equal(Text.Get("ObjectStorageDeleteStepCollect"), model.Heading);
                    Assert.Equal(Text.Format("ObjectStorageDeleteCollectProgress", 2500), model.ProgressText);
                    Assert.False(list.IsVisible);
                    Assert.False(confirm.IsVisible);

                    const int total = 11920;
                    collected.SetResult(new(Enumerable.Range(0, total).Select(index => $"releases/应用 1/{index}").ToArray()));
                    await deleting.Task.WaitAsync(token);
                    Assert.NotNull(deleteProgress);
                    deleteProgress.Report(5000);
                    collectProgress.Report(11000);
                    await WindowPreviewCapture.VerifyAsync(dialog, $"object-delete-progress-{language}");
                    Assert.True(progress.IsVisible);
                    Assert.False(progress.IsIndeterminate);
                    Assert.Equal(total, progress.Maximum);
                    Assert.Equal(5000, progress.Value);
                    Assert.Equal(Text.Get("ObjectStorageDeleteStepDelete"), model.Heading);
                    Assert.Equal(Text.Format("ObjectStorageDeleteBatchProgress", 5000, total), model.ProgressText);

                    if (stopWithCaption)
                        WindowTestVisuals.Invoke(ObjectStorageDialogTestVisuals.CaptionClose(dialog));
                    else
                        deleted.SetResult();
                    while (model.IsRunning) await WpfTestFixture.DrainDispatcherAsync();
                    Assert.True(dialog.IsVisible);
                    await WindowPreviewCapture.VerifyAsync(dialog, $"object-delete-result-{language}");
                    Assert.Equal(stopWithCaption ? 5000 : total, progress.Value);
                    Assert.Equal(Text.Get(stopWithCaption ? "ObjectStorageDeleteStopped" : "ObjectStorageDeleteCompleted"), model.Outcome);
                    Assert.Equal(Text.Get("Close"), dismiss.Content);
                    SecretInputTests.ToggleWithKeyboard(dismiss);
                }
                finally { lifetime.Cancel(); }
            });
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            owner.Close();
            CultureInfo.CurrentUICulture = previous;
            themes.SetTheme(UiTheme.Light);
        }
    });
}
