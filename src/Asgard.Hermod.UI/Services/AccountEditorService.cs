using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Services;

/// <summary>模态账号窗口关闭后等待取消完成，不能遗留回环监听。</summary>
public sealed class AccountEditorService(Func<Window?> owner, IAccountService accounts, IWorkspaceSyncService sync) : IAccountEditor
{
    /// <inheritdoc />
    public Task OpenAsync() => ShowAsync(false);
    /// <inheritdoc />
    public Task OpenForSignInAsync() => ShowAsync(true);

    /// <summary>本地模式入口在窗口可取消之后启动浏览器，已登录入口只展示账号与同步。</summary>
    private async Task ShowAsync(bool signIn)
    {
        AccountWindow? window = null;
        await using var model = new AccountViewModel(accounts, sync, download => RequestPassword(window!, download));
        window = new AccountWindow(model) { Owner = owner() };
        if (signIn) window.Loaded += (_, _) => model.SignInCommand.Execute(null);
        window.ShowDialog();
    }

    /// <summary>只在用户点击同步后索取密码；返回后立即清除弹窗状态，不在账号页保留秘密。</summary>
    private static string? RequestPassword(Window ownerWindow, bool download)
    {
        using var model = new SyncPasswordViewModel(download);
        var dialog = new SyncPasswordWindow(model) { Owner = ownerWindow };
        return dialog.ShowDialog() == true ? model.Password : null;
    }
}
