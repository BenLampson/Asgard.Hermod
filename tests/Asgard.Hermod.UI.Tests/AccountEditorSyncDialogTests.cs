using System.Diagnostics;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证生产账号编辑器的真实按钮与嵌套密码弹窗，不访问浏览器或线上服务。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AccountEditorSyncDialogTests(WpfTestFixture fixture)
{
    /// <summary>确认后才执行对应同步，取消不触及服务，弹窗结束立即清除密码。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Editor_SyncButtonPromptsBeforeCallingService(bool download, bool cancel) => fixture.RunAsync(async () =>
    {
        var service = new AccountUiTestService();
        await service.SignInAsync();
        string? receivedPassword = null;
        service.SyncAction = (password, _) => { receivedPassword = password; return Task.CompletedTask; };
        var editor = new AccountEditorService(() => null, service, service);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        AccountWindow? account = null;
        SyncPasswordWindow? prompt = null;
        SyncPasswordViewModel? passwordModel = null;
        int phase = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(8), "账号编辑器没有完成同步交互。");
                if (phase == 0)
                {
                    account = Application.Current.Windows.OfType<AccountWindow>().SingleOrDefault();
                    if (account is null) return;
                    account.Opacity = 0;
                    var model = Assert.IsType<AccountViewModel>(account.DataContext);
                    Assert.Empty(WindowTestVisuals.Descendants<SecretInput>(account));
                    var button = Assert.Single(WindowTestVisuals.Descendants<Button>(account),
                        item => item.Command == (download ? model.DownloadCommand : model.UploadCommand));
                    phase = 1;
                    // 单独排入消息循环，计时器才能继续驱动嵌套的模态弹窗。
                    account.Dispatcher.InvokeAsync(() => WindowTestVisuals.Invoke(button));
                }
                else if (phase == 1)
                {
                    prompt = Application.Current.Windows.OfType<SyncPasswordWindow>().SingleOrDefault();
                    if (prompt is null) return;
                    prompt.Opacity = 0;
                    Assert.Same(account, prompt.Owner);
                    passwordModel = Assert.IsType<SyncPasswordViewModel>(prompt.DataContext);
                    Assert.Equal(download, passwordModel.IsDownload);
                    Assert.Empty(passwordModel.Password);
                    Assert.Equal(0, service.Uploads + service.Downloads);
                    passwordModel.Password = "password-for-sync";
                    passwordModel.ConfirmRestore = download;
                    phase = 2;
                    if (cancel) WindowTestVisuals.Invoke(Assert.IsType<Button>(prompt.FindName("CancelButton")));
                    else WindowTestVisuals.Invoke(Assert.IsType<Button>(prompt.FindName("ConfirmButton")));
                }
                else
                {
                    var model = Assert.IsType<AccountViewModel>(account!.DataContext);
                    if (model.IsBusy) return;
                    Assert.Equal(cancel ? null : "password-for-sync", receivedPassword);
                    Assert.Equal(!cancel && download ? 1 : 0, service.Downloads);
                    Assert.Equal(!cancel && !download ? 1 : 0, service.Uploads);
                    Assert.Empty(passwordModel!.Password);
                    Assert.Empty(Assert.IsType<SecretInput>(prompt!.FindName("PasswordInput")).Value);
                    timer.Stop();
                    account.Close();
                    finished.TrySetResult();
                }
            }
            catch (Exception error)
            {
                timer.Stop();
                prompt?.Close(); account?.Close();
                finished.TrySetException(error);
            }
        };
        try
        {
            timer.Start();
            await editor.OpenAsync();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            timer.Stop();
            if (prompt?.IsVisible == true) prompt.Close();
            if (account?.IsVisible == true) account.Close();
        }
    });
}
