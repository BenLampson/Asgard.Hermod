namespace Asgard.Hermod.UI.Tests;

/// <summary>使用真实文件系统验证占用统计、跨服务租约、遗留缓存和安全清理边界。</summary>
public sealed class TemporaryFileStorageTests
{
    /// <summary>跨实例扫描也能识别正在下载的文件，正文和中间文件分开统计。</summary>
    [Fact]
    public async Task Inspect_CountsActualBytesAndCleanupSkipsActiveRequestAcrossInstances()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        using var file = environment.Storage.CreateFile("hello.txt");
        await File.WriteAllBytesAsync(file.Path, new byte[100], TestContext.Current.CancellationToken);
        string partial = Path.Combine(Path.GetDirectoryName(file.Path)!, ".hello.txt." + Guid.NewGuid().ToString("N") + ".download");
        await File.WriteAllBytesAsync(partial, new byte[50], TestContext.Current.CancellationToken);
        using var otherInstance = new LocalTemporaryFileStorage(environment.Storage.RootPath);
        var snapshot = await otherInstance.InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(150, snapshot.TotalBytes);
        Assert.Equal(2, snapshot.FileCount);
        Assert.Equal(150, snapshot.InUseBytes);
        Assert.Equal(0, snapshot.CleanableBytes);
        Assert.Equal(100, snapshot.Usage.Single(usage => usage.Category == TemporaryFileCategory.Download).Bytes);
        Assert.Equal(50, snapshot.Usage.Single(usage => usage.Category == TemporaryFileCategory.Processing).Bytes);
        var result = await otherInstance.CleanupAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, result.DeletedFileCount);
        Assert.Equal(2, result.SkippedFileCount);
        Assert.True(File.Exists(file.Path));
        Assert.True(File.Exists(partial));
        file.Dispose();
        Assert.False(Directory.Exists(Path.GetDirectoryName(file.Path)));
    }

    /// <summary>外部程序缓存即使正文句柄关闭也在本会话保护，退出后可安全清理。</summary>
    [Fact]
    public async Task ExternalCache_IsProtectedUntilServiceExitAndRetainedForNextSession()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        string path;
        using (var file = environment.Storage.CreateFile("image.png"))
        {
            path = file.Path;
            await File.WriteAllBytesAsync(path, new byte[128], TestContext.Current.CancellationToken);
            file.RetainForExternalUse();
        }
        var snapshot = await environment.Storage.InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(128, snapshot.Usage.Single(usage => usage.Category == TemporaryFileCategory.Cache).Bytes);
        Assert.Equal(128, snapshot.InUseBytes);
        environment.Storage.Dispose();
        Assert.True(File.Exists(path));
        using var reopened = new LocalTemporaryFileStorage(environment.Storage.RootPath);
        snapshot = await reopened.InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(128, snapshot.CleanableBytes);
        var cleaned = await reopened.CleanupAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(128, cleaned.FreedBytes);
        Assert.Equal(1, cleaned.DeletedFileCount);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.EnumerateDirectories(reopened.RootPath));
    }

    /// <summary>程序已打开后，分类标记失败也不能使释放操作删除它的正文。</summary>
    [Fact]
    public async Task ExternalCache_PreservesFileWhenCategoryUpdateFails()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var file = environment.Storage.CreateFile("opened.txt");
        await File.WriteAllTextAsync(file.Path, "opened", TestContext.Current.CancellationToken);
        string marker = Path.Combine(Path.GetDirectoryName(file.Path)!, ".hermod-category");
        using (var handle = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => file.RetainForExternalUse());
        file.Dispose();
        Assert.True(File.Exists(file.Path));
        Assert.Equal(0, (await environment.Storage.InspectAsync(TestContext.Current.CancellationToken)).CleanableBytes);
    }

    /// <summary>只有应用根目录的 GUID 请求参与清理，配置、工作区和嵌套目录保持原样。</summary>
    [Fact]
    public async Task Cleanup_PreservesConfigurationWorkspacesAndUnknownDirectories()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        environment.Storage.EnsureDirectory();
        string safe = LegacyFile(environment.Storage.RootPath, "old.txt");
        string config = Path.Combine(environment.DirectoryPath, "hermod.db");
        string nested = Path.Combine(environment.Storage.RootPath, Guid.NewGuid().ToString("N"), "workspace");
        Directory.CreateDirectory(nested);
        string workspace = Path.Combine(nested, "important.txt");
        string unknown = Path.Combine(environment.Storage.RootPath, "user-folder");
        Directory.CreateDirectory(unknown);
        string unknownFile = Path.Combine(unknown, "keep.txt");
        foreach (string path in new[] { safe, config, workspace, unknownFile })
            await File.WriteAllTextAsync(path, "keep", TestContext.Current.CancellationToken);
        var result = await environment.Storage.CleanupAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, result.DeletedFileCount);
        Assert.False(File.Exists(safe));
        Assert.True(File.Exists(config));
        Assert.True(File.Exists(workspace));
        Assert.True(File.Exists(unknownFile));
        Assert.True(result.SkippedFileCount > 0);
    }

    /// <summary>扫描后被其它程序锁定的缓存，在清理时必须重新检测而跳过。</summary>
    [Fact]
    public async Task Cleanup_RechecksFileLocksAfterSnapshot()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        environment.Storage.EnsureDirectory();
        string path = LegacyFile(environment.Storage.RootPath, "locked.txt");
        await File.WriteAllBytesAsync(path, new byte[123], TestContext.Current.CancellationToken);
        Assert.Equal(123, (await environment.Storage.InspectAsync(TestContext.Current.CancellationToken)).CleanableBytes);
        using (var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await environment.Storage.CleanupAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(0, result.FreedBytes);
            Assert.Equal(1, result.SkippedFileCount);
            Assert.Equal(123, (await environment.Storage.InspectAsync(TestContext.Current.CancellationToken)).InUseBytes);
        }
        Assert.Equal(1, (await environment.Storage.CleanupAsync(cancellationToken: TestContext.Current.CancellationToken)).DeletedFileCount);
    }

    /// <summary>自动维护只处理七天以前的闲置请求，手动维护可清理新的闲置缓存。</summary>
    [Fact]
    public async Task StartupCleanup_PreservesRecentCacheAndDeletesExpiredCache()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        environment.Storage.EnsureDirectory();
        string old = LegacyFile(environment.Storage.RootPath, "old.txt");
        string recent = LegacyFile(environment.Storage.RootPath, "recent.txt");
        await File.WriteAllTextAsync(old, "old", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(recent, "new", TestContext.Current.CancellationToken);
        Directory.SetCreationTimeUtc(Path.GetDirectoryName(old)!, DateTime.UtcNow.AddDays(-8));
        var result = await environment.Storage.CleanupAsync(expiredOnly: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, result.DeletedFileCount);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    /// <summary>下载键不能覆盖租约与分类标记，也不能控制本机目录。</summary>
    [Theory]
    [InlineData("../../CON.txt")]
    [InlineData(".hermod-lease")]
    [InlineData(".hermod-category")]
    public void Allocation_UsesSafeNamesAndOwnsLease(string name)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        using var file = environment.Storage.CreateFile(name);
        Assert.Equal(environment.Storage.RootPath, Path.GetDirectoryName(Path.GetDirectoryName(file.Path)!));
        Assert.DoesNotContain("..", Path.GetFileName(file.Path));
        Assert.NotEqual(".hermod-lease", Path.GetFileName(file.Path));
        Assert.NotEqual(".hermod-category", Path.GetFileName(file.Path));
    }

    /// <summary>验证取消令牌，清理开始前取消时不删除任何文件。</summary>
    [Fact]
    public async Task Cancellation_LeavesCacheUntouched()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        environment.Storage.EnsureDirectory();
        string path = LegacyFile(environment.Storage.RootPath, "safe.txt");
        await File.WriteAllTextAsync(path, "safe", TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => environment.Storage.CleanupAsync(cancellationToken: cancellation.Token));
        Assert.True(File.Exists(path));
    }

    /// <summary>兼容上一版本的 GUID 文件缓存目录，不需要迁移用户正文。</summary>
    private static string LegacyFile(string root, string name)
    {
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name);
    }
}
