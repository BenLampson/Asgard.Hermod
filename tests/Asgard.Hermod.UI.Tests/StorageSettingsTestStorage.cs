namespace Asgard.Hermod.UI.Tests;

/// <summary>故障及迟到响应边界替身，用于界面生命周期测试。</summary>
internal sealed class StorageSettingsTestStorage : ITemporaryFileStorage
{
    /// <summary>可注入扫描失败或未完成响应。</summary>
    internal Func<CancellationToken, Task<TemporaryStorageSnapshot>>? Inspect { get; set; }
    /// <summary>可注入清理失败。</summary>
    internal Func<CancellationToken, Task<TemporaryStorageCleanupResult>>? Cleanup { get; set; }
    /// <summary>记录是否真正发出清理调用。</summary>
    internal int CleanupCalls { get; private set; }
    /// <inheritdoc />
    public string RootPath => Path.Combine(Path.GetTempPath(), "Hermod-test-storage");
    /// <inheritdoc />
    public void EnsureDirectory() { }
    /// <inheritdoc />
    public IManagedTemporaryFile CreateFile(string name) => throw new NotSupportedException();
    /// <inheritdoc />
    public Task<TemporaryStorageSnapshot> InspectAsync(CancellationToken cancellationToken = default) =>
        Inspect?.Invoke(cancellationToken) ?? Task.FromResult(new TemporaryStorageSnapshot(1024, 1, 0, 0,
            [new(TemporaryFileCategory.Download, 0, 0), new(TemporaryFileCategory.Cache, 1024, 1), new(TemporaryFileCategory.Processing, 0, 0)]));
    /// <inheritdoc />
    public Task<TemporaryStorageCleanupResult> CleanupAsync(bool expiredOnly = false, CancellationToken cancellationToken = default)
    {
        CleanupCalls++;
        return Cleanup?.Invoke(cancellationToken) ?? Task.FromResult(new TemporaryStorageCleanupResult(1024, 1, 0));
    }
}
