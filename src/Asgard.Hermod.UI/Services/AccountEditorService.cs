using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Services;

/// <summary>模态账号窗口关闭后等待取消完成，不能遗留回环监听。</summary>
public sealed class AccountEditorService(Func<Window?> owner, IAccountService accounts, IWorkspaceSyncService sync,
    IConfigurationSyncService? automatic = null, IAccountRegistrationService? registration = null) : IAccountEditor
{
    /// <inheritdoc />
    public Task OpenAsync() => ShowAsync(owner());
    /// <inheritdoc />
    public Task OpenAsync(Window? ownerWindow) => ShowAsync(ownerWindow);
    /// <inheritdoc />
    public Task OpenForSignInAsync() => ShowAsync(owner());

    /// <inheritdoc />
    public Task OpenConflictsAsync(Window? ownerWindow = null) => ShowAsync(ownerWindow ?? owner(), resolveConflicts: true);

    /// <summary>先让用户选择登录或注册，只有明确点击登录才启动浏览器。</summary>
    private async Task ShowAsync(Window? ownerWindow, bool resolveConflicts = false)
    {
        AccountWindow? window = null;
        await using var model = new AccountViewModel(accounts, sync, download => RequestPassword(window!, download, automatic is not null), automatic,
            conflicts => RequestMerge(window!, conflicts), registration is null ? null : () => RequestRegistrationAsync(window!));
        window = new AccountWindow(model) { Owner = ownerWindow };
        if (resolveConflicts)
        {
            // 先显示拥有状态和取消操作的账户窗口，再直接打开逐项选择界面。
            window.Loaded += async (_, _) =>
            {
                if (!model.MergeCommand.CanExecute(null) && model.SyncNowCommand.CanExecute(null))
                    await model.SyncNowCommand.ExecuteAsync();
                if (model.MergeCommand.CanExecute(null)) await model.MergeCommand.ExecuteAsync();
            };
        }
        window.ShowDialog();
    }

    /// <summary>等待注册窗口请求取消完成；返回值只表示用户是否明确选择登录。</summary>
    private async Task<bool> RequestRegistrationAsync(Window ownerWindow)
    {
        await using var model = new AccountRegistrationViewModel(registration!);
        var dialog = new AccountRegistrationWindow(model) { Owner = ownerWindow };
        return dialog.ShowDialog() == true;
    }

    /// <summary>只在用户点击同步后索取密码；返回后立即清除弹窗状态，不在账号页保留秘密。</summary>
    private static string? RequestPassword(Window ownerWindow, bool download, bool automatic)
    {
        using var model = new SyncPasswordViewModel(download, automatic);
        var dialog = new SyncPasswordWindow(model) { Owner = ownerWindow };
        return dialog.ShowDialog() == true ? model.Password : null;
    }
    /// <summary>返回逐项明确选择，取消不会丢弃任一端。</summary>
    private static IReadOnlyDictionary<string, bool>? RequestMerge(Window ownerWindow, IReadOnlyList<ConfigurationConflict> conflicts)
    {
        using var model = new ConfigurationMergeViewModel(conflicts);
        var dialog = new ConfigurationMergeWindow(model) { Owner = ownerWindow };
        return dialog.ShowDialog() == true ? model.Choices : null;
    }
}
