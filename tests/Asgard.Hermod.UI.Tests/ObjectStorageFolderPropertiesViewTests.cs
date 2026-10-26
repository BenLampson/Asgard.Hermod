using System.Globalization;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>双语和主题下验证真实属性窗体、键盘开始、未知总量进度、完成属性及缩放布局。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageFolderPropertiesViewTests(WpfTestFixture fixture)
{
    /// <summary>仅实际点击开始后列举对象，完成后显示大小和数量，迟到进度不得覆盖完成说明。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light, 560)]
    [InlineData("zh-Hans", UiTheme.Dark, 460)]
    public Task Properties_RenderWarningProgressAndResult(string language, UiTheme theme, double width) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        var response = new TaskCompletionSource<ObjectStorageFolderStatistics>(TaskCreationOptions.RunContinuationsAsynchronously);
        IProgress<ObjectStorageFolderStatistics>? reporter = null;
        using var model = new ObjectStorageFolderPropertiesViewModel(new("bucket", "region"), "folder/", (progress, _) =>
        {
            reporter = progress; return response.Task;
        });
        var dialog = new ObjectStorageFolderPropertiesWindow(model) { Width = width, Opacity = 0 };
        var owner = ObjectStorageDialogTestVisuals.CreateOwner();
        try
        {
            owner.Show(); dialog.Owner = owner;
            await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                Assert.Equal(Text.Get("FolderProperties"), dialog.Title);
                Assert.Equal(Text.Get("FolderStatisticsWarning"), model.Status); Assert.Null(reporter);
                await WindowPreviewCapture.VerifyAsync(dialog, $"folder-properties-warning-{language}");
                var start = Assert.IsType<Button>(dialog.FindName("StartButton"));
                Assert.True(start.Focusable);
                SecretInputTests.ToggleWithKeyboard(start);
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.NotNull(reporter); Assert.True(model.IsRunning);
                reporter.Report(new(100, 10, 1024));
                await WpfTestFixture.DrainDispatcherAsync();
                var progress = Assert.IsType<ProgressBar>(dialog.FindName("ScanProgress"));
                Assert.True(progress.IsVisible); Assert.True(progress.IsIndeterminate);
                Assert.False(Assert.IsType<Grid>(dialog.FindName("PropertiesPanel")).IsVisible);
                Assert.Equal(Text.Format("FolderStatisticsProgress", 100, 10, "1 KB"), model.Status);
                await WindowPreviewCapture.VerifyAsync(dialog, $"folder-properties-progress-{language}");
                response.SetResult(new(100, 10, 1024));
                while (model.StartCommand.IsRunning) await WpfTestFixture.DrainDispatcherAsync();
                reporter.Report(new(1, 1, 1));
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.True(model.IsComplete); Assert.False(progress.IsVisible);
                Assert.Equal(Text.Get("FolderStatisticsCompleted"), model.Status);
                Assert.True(Assert.IsType<Grid>(dialog.FindName("PropertiesPanel")).IsVisible);
                Assert.Equal(Text.Format("FolderStatisticsContains", 100, 10), model.Contains);
                Assert.False(start.IsVisible);
                dialog.Height = 420;
                await WindowPreviewCapture.VerifyAsync(dialog, $"folder-properties-result-{language}");
                model.CancelCommand.Execute(null);
            });
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close(); owner.Close();
            CultureInfo.CurrentUICulture = previous; themes.SetTheme(UiTheme.Light);
        }
    });
}
