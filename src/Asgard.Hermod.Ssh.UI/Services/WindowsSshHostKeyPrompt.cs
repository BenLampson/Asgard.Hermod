using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.Ssh.UI.Services;

/// <summary>协议线程通过 Dispatcher 请求核验；取消测试也会关闭待核验窗口。</summary>
public sealed class WindowsSshHostKeyPrompt(Func<Window?> owner) : ISshHostKeyPrompt
{
    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(SshHostKeyChallenge challenge, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatcher = Application.Current?.Dispatcher ?? throw Text.Exception("SshHostNotTrusted");
        var operation = dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = new SshHostKeyWindow(challenge)
            {
                Owner = Application.Current?.Windows.OfType<Window>()
                .FirstOrDefault(candidate => candidate.IsActive) ?? owner()
            };
            using var registration = cancellationToken.Register(() => dispatcher.BeginInvoke(() =>
            {
                if (window.IsVisible) window.DialogResult = false;
            }));
            return window.ShowDialog() == true && !cancellationToken.IsCancellationRequested;
        });
        return await operation.Task.ConfigureAwait(false);
    }
}
