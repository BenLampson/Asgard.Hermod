using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证同步命令按次索取密码，取消与账号变化均不会访问云端。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AccountSyncPromptTests(WpfTestFixture fixture)
{
    /// <summary>未登录不弹窗；取消后可以重试，每次都重新请求密码。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Sync_CancelDoesNotCallServiceAndRetryRequestsNewPassword(bool download) => fixture.RunAsync(async () =>
    {
        var service = new AccountUiTestService();
        int prompts = 0;
        string? answer = null;
        var passwords = new List<string>();
        service.SyncAction = (password, _) => { passwords.Add(password); return Task.CompletedTask; };
        await using var model = new AccountViewModel(service, service, action =>
        {
            Assert.Equal(download, action);
            prompts++;
            return answer;
        });
        var command = download ? model.DownloadCommand : model.UploadCommand;
        await command.ExecuteAsync();
        Assert.Equal(0, prompts);
        await service.SignInAsync();
        await command.ExecuteAsync();
        Assert.Equal(1, prompts);
        Assert.Equal(0, service.Uploads + service.Downloads);
        Assert.Equal(Text.Get("AccountOperationCancelled"), model.Message);
        Assert.False(model.IsError);
        Assert.True(command.CanExecute(null));
        answer = "first-sync-password";
        await command.ExecuteAsync();
        answer = "second-sync-password";
        await command.ExecuteAsync();
        Assert.Equal(3, prompts);
        Assert.Equal(new[] { "first-sync-password", "second-sync-password" }, passwords);
        Assert.Equal(download ? 2 : 0, service.Downloads);
        Assert.Equal(download ? 0 : 2, service.Uploads);
    });

    /// <summary>弹窗期间保持账号操作互斥，生命周期取消或身份变化后丢弃用户输入。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Sync_CancelOrAccountChangeDuringPromptPreventsUpload(bool signOut) => fixture.RunAsync(async () =>
    {
        var service = new AccountUiTestService();
        await service.SignInAsync();
        AccountViewModel? model = null;
        model = new AccountViewModel(service, service, download =>
        {
            Assert.True(model!.IsBusy);
            Assert.False(model.SignInCommand.CanExecute(null));
            Assert.False(model.SignOutCommand.CanExecute(null));
            Assert.False(model.DownloadCommand.CanExecute(null));
            if (signOut) _ = service.SignOutAsync();
            else model.CancelCommand.Execute(null);
            return "password-for-sync";
        });
        await using (model)
        {
            await model.UploadCommand.ExecuteAsync();
            Assert.Equal(0, service.Uploads);
            Assert.False(model.IsError);
            Assert.Equal(Text.Get("AccountOperationCancelled"), model.Message);
        }
    });
}
