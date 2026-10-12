using System.Diagnostics;
using System.IO;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Services;

/// <summary>使用所属设置窗口确认清理，目录通过资源管理器打开。</summary>
public sealed class WindowsStorageSettingsDialogs(Func<Window?> owner) : IStorageSettingsDialogs
{
    /// <inheritdoc />
    public Task<bool> ConfirmCleanupAsync(long estimatedBytes, int fileCount, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new TemporaryStorageCleanupWindow(estimatedBytes) { Owner = owner() };
        using var registration = cancellationToken.Register(() => window.Dispatcher.BeginInvoke(() => { if (window.IsVisible) window.Close(); }));
        bool accepted = window.ShowDialog() == true;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(accepted);
    }
    /// <inheritdoc />
    public void OpenFolder(string path)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        { UseShellExecute = false };
        info.ArgumentList.Add(path);
        using var process = Process.Start(info);
    }
}
