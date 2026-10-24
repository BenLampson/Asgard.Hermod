using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.Ssh.UI.Services;

/// <summary>会话确认使用公共窗口与资源，默认焦点落在取消。</summary>
public sealed class WindowsSshWorkspaceDialogs(Func<Window?> owner) : ISshWorkspaceDialogs
{
    /// <inheritdoc />
    public Task<bool> ConfirmInstanceCloseAsync(string instanceName, int consoles, int forwards, int transfers)
        => Task.FromResult(new ConfirmationDialog(Text.Get("SshCloseInstanceTitle"), Text.Format("SshCloseInstanceImpact", instanceName, consoles, forwards, transfers), Text.Get("SshCloseConfirm"), true) { Owner = owner() }.ShowDialog() == true);
    /// <inheritdoc />
    public Task<bool> ConfirmConsoleCloseAsync(string instanceName, string consoleName)
        => Task.FromResult(new ConfirmationDialog(Text.Get("SshCloseConsoleTitle"), Text.Format("SshCloseConsoleImpact", consoleName, instanceName), Text.Get("SshCloseConsoleConfirm"), true) { Owner = owner() }.ShowDialog() == true);
    /// <inheritdoc />
    public Task<bool> ConfirmActionAsync(string title, string detail, string preview) => Task.FromResult(new ConfirmationDialog(title, detail, Text.Get("SshActionConfirm"), preview: preview) { Owner = owner() }.ShowDialog() == true);
    /// <inheritdoc />
    public Task<bool> ConfirmDockerActionAsync(string title, string detail, string preview, CancellationToken token)
        => ConfirmCancelableActionAsync(title, detail, token);
    /// <inheritdoc />
    public Task<bool> ConfirmKubernetesActionAsync(string title, string detail, string preview, CancellationToken token)
        => ConfirmCancelableActionAsync(title, detail, token);

    private Task<bool> ConfirmCancelableActionAsync(string title, string detail, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dialog = new ConfirmationDialog(title, detail, title) { Owner = owner() };
        using var canceled = token.Register(() => dialog.Dispatcher.BeginInvoke(() => { if (dialog.IsVisible) dialog.Close(); }));
        bool accepted = dialog.ShowDialog() == true;
        return Task.FromResult(accepted && !token.IsCancellationRequested);
    }
    /// <inheritdoc />
    public string? ChooseUpload()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = false };
        return dialog.ShowDialog(owner()) == true ? dialog.FileName : null;
    }
    /// <inheritdoc />
    public string? ChooseDownload(string name)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = System.IO.Path.GetFileName(name), OverwritePrompt = true };
        return dialog.ShowDialog(owner()) == true ? dialog.FileName : null;
    }
    /// <inheritdoc />
    public string? RequestName(string title, string initial)
    {
        var dialog = new Views.SshNameDialog(title, initial) { Owner = owner() };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }
    /// <inheritdoc />
    public Task<bool> ConfirmCloseAsync(string instanceName)
    {
        var dialog = new ConfirmationDialog(Text.Get("SshCloseTitle"), Text.Get("SshCloseDescription"), Text.Get("SshCloseConfirm"), true)
        { Owner = owner() };
        return Task.FromResult(dialog.ShowDialog() == true);
    }
}
