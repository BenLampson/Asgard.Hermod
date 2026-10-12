using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Services;

/// <summary>模态账号窗口关闭后等待取消完成，不能遗留回环监听。</summary>
public sealed class AccountEditorService(Func<Window?> owner, IAccountService accounts, IWorkspaceSyncService sync) : IAccountEditor
{
    /// <inheritdoc />
    public async Task OpenAsync()
    {
        await using var model = new AccountViewModel(accounts, sync);
        new AccountWindow(model) { Owner = owner() }.ShowDialog();
    }
}
