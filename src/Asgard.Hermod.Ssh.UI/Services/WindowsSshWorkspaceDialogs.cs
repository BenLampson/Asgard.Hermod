using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.Ssh.UI.Services;

/// <summary>会话确认使用公共窗口与资源，默认焦点落在取消。</summary>
public sealed class WindowsSshWorkspaceDialogs(Func<Window?> owner) : ISshWorkspaceDialogs
{
    /// <inheritdoc />
    public Task<bool> ConfirmCloseAsync(string instanceName)
    {
        var dialog = new ConfirmationDialog(Text.Get("SshCloseTitle"), Text.Get("SshCloseDescription"), Text.Get("SshCloseConfirm"), true)
        { Owner = owner() };
        return Task.FromResult(dialog.ShowDialog() == true);
    }
}
