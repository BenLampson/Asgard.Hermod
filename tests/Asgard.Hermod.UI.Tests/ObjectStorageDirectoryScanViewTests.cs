using System.Globalization;
using System.Windows.Input;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>按 Pencil 验证扫描、暂停和完成状态，涵盖双语、主题、小窗口及键盘操作。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageDirectoryScanViewTests(WpfTestFixture fixture)
{
    /// <summary>扫描入口及继续、取消按钮在窄宽度保持可见，暂停时不发后续请求。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light, 700, true)]
    [InlineData("en", UiTheme.Dark, 440, true)]
    [InlineData("zh-Hans", UiTheme.Light, 440, true)]
    [InlineData("zh-Hans", UiTheme.Dark, 700, true)]
    [InlineData("en", UiTheme.Light, 440, false)]
    [InlineData("zh-Hans", UiTheme.Dark, 700, false)]
    public Task ScanStates_RenderAndKeyboardContinues(string language, UiTheme theme, double width, bool continueScan) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        var allowPause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("folder/", true)])),
            ScanHandler = async (_, confirm, progress, token) =>
            {
                progress?.Report(new(5000, 5));
                await allowPause.Task.WaitAsync(token);
                // 业务服务在后台继续，状态及操作仍必须安全回到 WPF 上下文。
                bool accepted = await Task.Run(() => confirm(new(10001, 11), token), token);
                Assert.True(accepted);
                return new([new("folder/", true, LastModified: DateTimeOffset.UtcNow)], 10001);
            },
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.ScannedFiles) && model.ScannedFiles == 5000) scanning.TrySetResult();
            if (args.PropertyName == nameof(model.IsScanPaused) && model.IsScanPaused) paused.TrySetResult();
        };
        var view = new ObjectStorageListView { DataContext = model };
        var window = new HermodWindow { Title = "OSS", Width = width, Height = 560, Content = view };
        try
        {
            await model.InitializeAsync(TestContext.Current.CancellationToken);
            WindowPreviewCapture.Show(window);
            await WindowPreviewCapture.VerifyAsync(window, $"directory-scan-ready-{language}-{theme}-{width}");
            var start = Assert.Single(WindowTestVisuals.Descendants<Button>(view), button => ReferenceEquals(button.Command, model.ScanDirectoryCommand));
            Assert.Equal(32, start.ActualHeight);
            Assert.True(start.Command.CanExecute(null));
            var scan = model.ScanDirectoryAsync(TestContext.Current.CancellationToken);
            await scanning.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await WindowPreviewCapture.VerifyAsync(window, $"directory-scan-running-{language}-{theme}-{width}");
            Assert.False(start.IsEnabled);
            Assert.Equal(Text.Format("DirectoryScanProgress", 5000), model.ScanStatus);
            Assert.False(model.IsScanComplete);
            allowPause.SetResult();
            await paused.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await WindowPreviewCapture.VerifyAsync(window, $"directory-scan-paused-{language}-{theme}-{width}");
            Assert.Equal(Text.Format("DirectoryScanLarge", 10001), model.ScanStatus);
            foreach (var button in WindowTestVisuals.Descendants<Button>(view).Where(button => button.IsVisible
                && (ReferenceEquals(button.Command, model.CancelDirectoryScanCommand) || ReferenceEquals(button.Command, model.ContinueDirectoryScanCommand))))
            {
                var bounds = WindowTestVisuals.ScreenBounds(button);
                var container = WindowTestVisuals.ScreenBounds(view);
                Assert.True(bounds.Left >= container.Left && bounds.Right <= container.Right + 1);
                Assert.True(bounds.Bottom <= container.Bottom);
            }
            var resume = Assert.Single(WindowTestVisuals.Descendants<Button>(view), button => ReferenceEquals(button.Command, model.ContinueDirectoryScanCommand));
            var action = continueScan ? resume : Assert.Single(WindowTestVisuals.Descendants<Button>(view), button => ReferenceEquals(button.Command, model.CancelDirectoryScanCommand));
            Assert.True(action.Focusable);
            action.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(action), 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
            action.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(action), 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
            await scan.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await WindowPreviewCapture.VerifyAsync(window, $"directory-scan-{(continueScan ? "complete" : "canceled")}-{language}-{theme}-{width}");
            Assert.Equal(continueScan, model.IsScanComplete);
            Assert.False(model.IsScanPaused);
            Assert.False(resume.IsVisible);
            Assert.Equal(Text.Get(continueScan ? "DirectoryScanCompleted" : "DirectoryScanCanceled"), model.ScanStatus);
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentUICulture = previous;
            themes.SetTheme(UiTheme.Light);
        }
    });
}
