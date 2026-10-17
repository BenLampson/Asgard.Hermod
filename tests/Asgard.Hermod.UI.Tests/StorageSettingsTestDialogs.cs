namespace Asgard.Hermod.UI.Tests;

/// <summary>替换确认和 Explorer 边界，测试不弹系统窗口。</summary>
internal sealed class StorageSettingsTestDialogs : IStorageSettingsDialogs
{
    /// <summary>模拟用户是否确认。</summary>
    internal bool Accept { get; set; }
    /// <summary>确认时可锁定文件，模拟扫描后状态改变。</summary>
    internal Action? OnConfirm { get; set; }
    /// <summary>记录预计容量与文件数。</summary>
    internal List<(long Bytes, int Count)> Confirmations { get; } = [];
    /// <summary>记录打开目录的明确用户动作。</summary>
    internal string? OpenedPath { get; private set; }
    /// <inheritdoc />
    public Task<bool> ConfirmCleanupAsync(long estimatedBytes, int fileCount, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Confirmations.Add((estimatedBytes, fileCount));
        OnConfirm?.Invoke();
        return Task.FromResult(Accept);
    }
    /// <inheritdoc />
    public void OpenFolder(string path) => OpenedPath = path;
}
