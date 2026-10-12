namespace Asgard.Hermod.UI.Tests;

/// <summary>支持符号链接的环境中验证清理不进入链接目标，权限不足时明确跳过。</summary>
public sealed class TemporaryStorageLinkTests
{
    /// <summary>GUID 目录链接和正文文件链接都不能把配置或工作区纳入清理范围。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_DoesNotFollowLinksToWorkspace(bool directoryLink)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        environment.Storage.EnsureDirectory();
        string workspace = Path.Combine(environment.DirectoryPath, "workspace");
        Directory.CreateDirectory(workspace);
        string original = Path.Combine(workspace, "important.txt");
        await File.WriteAllTextAsync(original, "keep", TestContext.Current.CancellationToken);
        string request = Path.Combine(environment.Storage.RootPath, Guid.NewGuid().ToString("N"));
        string link = directoryLink ? request : Path.Combine(request, "linked.txt");
        if (!directoryLink) Directory.CreateDirectory(request);
        try
        {
            if (directoryLink) Directory.CreateSymbolicLink(link, workspace);
            else File.CreateSymbolicLink(link, original);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip("当前文件系统不支持建立测试符号链接。");
        }
        try
        {
            var result = await environment.Storage.CleanupAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(0, result.DeletedFileCount);
            Assert.True(result.SkippedFileCount > 0);
            Assert.Equal("keep", await File.ReadAllTextAsync(original, TestContext.Current.CancellationToken));
            Assert.True((await environment.Storage.InspectAsync(TestContext.Current.CancellationToken)).UnreadableItems > 0);
        }
        finally
        {
            // 只删除链接本身，后续隔离目录清理不需要穿过链接目标。
            if (directoryLink) Directory.Delete(link, recursive: false);
            else File.Delete(link);
        }
    }
}
