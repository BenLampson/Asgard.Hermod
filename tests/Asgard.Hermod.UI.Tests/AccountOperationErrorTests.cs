using System.Globalization;
using System.Windows.Automation;
using Asgard.Hermod.Identity;
using Asgard.Hermod.UI.Common;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证后台解密错误会进入真实窗口，并在重试和最小尺寸下保持正确反馈。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AccountOperationErrorTests(WpfTestFixture fixture)
{
    /// <summary>真实密码校验失败被异步按钮捕获，提示始终可见；下次操作清除错误状态。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    [InlineData("fr-FR", UiTheme.Light)]
    public Task Download_WrongPasswordShowsPinnedErrorAndAllowsRetry(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        AccountWindow? window = null;
        var themes = new ThemeService(Application.Current.Resources);
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            themes.SetTheme(theme);
            var encrypted = WorkspaceEncryption.Encrypt(new(1, [], []), "correct-sync-password", "owner");
            var service = new AccountUiTestService
            {
                SyncAction = (password, token) => Task.Run(() => WorkspaceEncryption.Decrypt(encrypted, password, "owner"), token),
            };
            await service.SignInAsync();
            string password = "wrong-sync-password";
            await using var model = new AccountViewModel(service, service, _ => password);
            window = new AccountWindow(model) { Width = 520, Height = 520 };
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var status = Assert.IsType<Border>(window.FindName("OperationStatus"));
            var message = Assert.IsType<TextBlock>(window.FindName("OperationMessage"));
            var form = Assert.IsType<ScrollViewer>(window.FindName("AccountForm"));
            Assert.Equal(Visibility.Collapsed, status.Visibility);
            var download = Assert.Single(WindowTestVisuals.Descendants<Button>(window), button => button.Command == model.DownloadCommand);
            form.ScrollToBottom();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            // 执行真实 ICommand 的 async void 入口，Dispatcher 未处理异常由 fixture 检查。
            WindowTestVisuals.Invoke(download);
            while (model.DownloadCommand.IsRunning) await Task.Delay(10, TestContext.Current.CancellationToken);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(model.IsError);
            Assert.False(model.IsBusy);
            Assert.Null(model.DownloadCommand.LastException);
            Assert.Equal(Text.Get("SyncDecryptionFailed"), message.Text);
            Assert.Same(window.FindResource("DangerBrush"), message.Foreground);
            Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(message));
            AssertStatusVisible(window, form, status, message);
            await WindowPreviewCapture.VerifyAsync(window, $"account-sync-error-compact-{theme}-{language}");
            form.ScrollToTop();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            AssertStatusVisible(window, form, status, message);
            window.Width = 620; window.Height = 630;
            await WindowPreviewCapture.VerifyAsync(window, $"account-sync-error-{theme}-{language}");
            AssertStatusVisible(window, form, status, message);

            var retry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            service.SyncAction = (_, _) => retry.Task;
            password = "correct-sync-password";
            Task operation = model.DownloadCommand.ExecuteAsync();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(model.IsBusy); Assert.False(model.IsError);
            Assert.Equal(Text.Get("AccountOperationInProgress"), message.Text);
            Assert.False(model.UploadCommand.CanExecute(null));
            Assert.Same(window.FindResource("AccentTextBrush"), message.Foreground);
            retry.SetResult();
            await operation;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.False(model.IsBusy); Assert.False(model.IsError);
            Assert.Equal(Text.Get("SyncDownloadSucceeded"), message.Text);
            Assert.True(model.DownloadCommand.CanExecute(null));
        }
        finally { window?.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; }
    });

    /// <summary>上传、下载的同步或异步未知错误统一使用安全资源，取消不会标记成失败。</summary>
    [Theory]
    [InlineData("en", false, false)]
    [InlineData("zh-Hans", true, true)]
    public Task Sync_UnknownFailureHidesRawDetailsAndCancellationClearsError(string language, bool upload, bool synchronous) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var service = new AccountUiTestService
            {
                SyncAction = (_, _) => synchronous ? throw new IOException("credential=private-value")
                    : Task.FromException(new IOException("credential=private-value")),
            };
            await service.SignInAsync();
            await using var model = new AccountViewModel(service, service, _ => "password-for-sync");
            AsyncRelayCommand command = upload ? model.UploadCommand : model.DownloadCommand;
            await command.ExecuteAsync();
            Assert.True(model.IsError);
            Assert.Equal(Text.Get("AccountOperationFailed"), model.Message);
            Assert.DoesNotContain("private-value", model.Message);
            Assert.False(model.IsBusy); Assert.True(command.CanExecute(null));
            service.SyncAction = (_, _) => Task.FromCanceled(new CancellationToken(true));
            await command.ExecuteAsync();
            Assert.False(model.IsError);
            Assert.Equal(Text.Get("AccountOperationCancelled"), model.Message);
            Assert.Null(command.LastException);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    });

    /// <summary>状态和完整文案位于窗体内部且不属于滚动表单，滚动不会隐藏结果。</summary>
    private static void AssertStatusVisible(AccountWindow window, ScrollViewer form, Border status, TextBlock message)
    {
        Assert.Equal(Visibility.Visible, status.Visibility);
        Assert.DoesNotContain(status, WindowTestVisuals.Descendants<Border>(form));
        Rect frame = WindowTestVisuals.ScreenBounds(WindowTestVisuals.Frame(window));
        Rect bounds = WindowTestVisuals.ScreenBounds(status);
        Assert.True(frame.Contains(bounds));
        Assert.True(bounds.Contains(WindowTestVisuals.ScreenBounds(message)));
        Assert.True(message.ActualHeight > 0);
        Assert.True(WindowTestVisuals.ScreenBounds(form).Bottom <= bounds.Top + 1);
    }
}
