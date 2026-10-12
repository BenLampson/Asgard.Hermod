using System.Diagnostics;
using System.IO;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Services;

/// <summary>显式打开始终重新读取规则，使保存和移除立即生效。</summary>
public sealed class WindowsFileOpenService(Func<Window?> owner, IFileOpenRuleStore rules) : IFileOpenService
{
    /// <inheritdoc />
    public bool CanPreview(string fileName) => FilePreviewPolicy.Limit(fileName) > 0;

    /// <inheritdoc />
    public Task OpenAsync(string localPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var rule = rules.Load().FirstOrDefault(item => item.Extension.Equals(Path.GetExtension(localPath), StringComparison.OrdinalIgnoreCase));
            using var process = Process.Start(FileProgramLaunch.CreateStartInfo(localPath, rule));
            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception.Data["Hermod.ResourceKey"] is not string) { throw Text.Exception("FileOpenFailed"); }
    }

    /// <inheritdoc />
    public async Task PreviewAsync(string localPath, CancellationToken cancellationToken = default)
    {
        FilePreviewContent content;
        try { content = await FilePreviewLoader.LoadAsync(localPath, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception.Data["Hermod.ResourceKey"] is not string) { throw Text.Exception("FilePreviewFailed"); }
        cancellationToken.ThrowIfCancellationRequested();
        var window = new FilePreviewWindow(content) { Owner = owner() };
        using var registration = cancellationToken.Register(() => window.Dispatcher.BeginInvoke(() => { if (window.IsVisible) window.Close(); }));
        window.ShowDialog();
    }
}
