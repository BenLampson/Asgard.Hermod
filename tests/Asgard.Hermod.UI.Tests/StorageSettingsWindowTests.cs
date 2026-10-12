using System.Globalization;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实 WPF 窗口核对双语、主题、统计绑定、缩小布局和清理确认。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class StorageSettingsWindowTests(WpfTestFixture fixture)
{
    /// <summary>存储分类可进入，统计与路径真实绑定，缩小窗口时底部清理操作仍可见。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Storage_RendersActualFilesAndFitsSmallWindow(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        SettingsWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var environment = new TemporaryStorageTestEnvironment();
            using var file = environment.Storage.CreateFile("live.json");
            await File.WriteAllBytesAsync(file.Path, new byte[8192], TestContext.Current.CancellationToken);
            string idleDir = Path.Combine(environment.Storage.RootPath, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(idleDir);
            await File.WriteAllBytesAsync(Path.Combine(idleDir, "old.txt"), new byte[32768], TestContext.Current.CancellationToken);
            using var model = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(), new LanguagePreferenceTestStore(),
                temporaryFiles: environment.Storage, storageDialogs: new StorageSettingsTestDialogs());
            await model.InitializeAsync(TestContext.Current.CancellationToken);
            window = new SettingsWindow(model);
            WindowPreviewCapture.Show(window);
            model.SelectCategoryCommand.Execute(model.Categories.Single(category => category.Id == "storage"));
            await WindowPreviewCapture.VerifyAsync(window, $"storage-settings-{language}-{theme}");
            var view = Assert.Single(WindowTestVisuals.Descendants<StorageSettingsView>(window));
            Assert.True(view.IsVisible);
            var storage = Assert.IsType<StorageSettingsViewModel>(model.Storage);
            Assert.Equal(40960, storage.Snapshot?.TotalBytes);
            Assert.Equal(8192, storage.Snapshot?.InUseBytes);
            var location = Assert.Single(WindowTestVisuals.Descendants<TextBox>(view));
            Assert.True(location.IsReadOnly);
            Assert.Equal(environment.Storage.RootPath, location.Text);
            var clean = Assert.Single(WindowTestVisuals.Descendants<Button>(view), button => ReferenceEquals(button.Command, storage.CleanupCommand));
            Assert.True(clean.IsEnabled);
            window.Width = 800; window.Height = 540;
            await WindowPreviewCapture.VerifyAsync(window, $"storage-settings-small-{language}-{theme}");
            var frame = WindowTestVisuals.Frame(window);
            Point origin = clean.TranslatePoint(new Point(), frame);
            Assert.InRange(origin.X + clean.ActualWidth, 1, frame.ActualWidth + 1);
            Assert.InRange(origin.Y + clean.ActualHeight, 1, frame.ActualHeight + 1);
        }
        finally { window?.Close(); CultureInfo.CurrentUICulture = previous; themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>确认窗口使用共享模板，取消和清理给出不同结果，正文容量来自参数。</summary>
    [Theory]
    [InlineData("en", false)]
    [InlineData("zh-Hans", true)]
    public Task Confirmation_ReportsExplicitChoiceAndLocalizes(string language, bool accept) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var window = new TemporaryStorageCleanupWindow(1024 * 1024)
            { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            bool? result = await ModalWindowTestInteraction.ShowAsync(window, async () =>
            {
                await WindowPreviewCapture.VerifyAsync(window, "storage-cleanup-confirmation-" + language);
                Assert.Equal(Text.Get("StorageConfirmCleanupTitle"), window.Title);
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(window), text => text.Text.Contains("1 MB", StringComparison.Ordinal));
                var button = Assert.Single(WindowTestVisuals.Descendants<Button>(window), item => Equals(item.Content, Text.Get(accept ? "StorageCleanup" : "Cancel")));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Assert.Equal(accept, result);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    });
}
