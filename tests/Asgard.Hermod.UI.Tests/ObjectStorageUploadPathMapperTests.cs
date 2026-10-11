namespace Asgard.Hermod.UI.Tests;

/// <summary>本地目录映射只读取元数据，并验证前缀、相对结构、取消及链接边界。</summary>
public sealed class ObjectStorageUploadPathMapperTests
{
    /// <summary>保留所选目录自身及子目录层次，独立文件只附加文件名。</summary>
    [Fact]
    public async Task UploadPaths_PreserveRelativeDirectoriesAndDoNotReadLockedFileContents()
    {
        using var area = new ObjectStorageDialogTestDirectory();
        string directory = Path.Combine(area.Root, "payload");
        string nested = Path.Combine(directory, "nested");
        Directory.CreateDirectory(nested);
        string file = Path.Combine(directory, "locked.bin");
        string nestedFile = Path.Combine(nested, "readme.md");
        string singleFile = Path.Combine(area.Root, "single.txt");
        await File.WriteAllTextAsync(file, "contents", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(nestedFile, "nested", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(singleFile, "single", TestContext.Current.CancellationToken);
        using var locked = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var mapped = await ObjectStorageUploadPathMapper.MapAsync([directory, singleFile, singleFile], "releases/",
            TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "releases/payload/locked.bin", "releases/payload/nested/readme.md", "releases/single.txt" },
            mapped.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.All(mapped, item => Assert.True(Path.IsPathFullyQualified(item.LocalPath)));
        Assert.Equal(8, mapped.Single(item => item.Key.EndsWith("locked.bin", StringComparison.Ordinal)).Size);
    }

    /// <summary>已取消的映射不枚举路径，也不返回部分文件。</summary>
    [Fact]
    public async Task UploadPaths_CancelledTokenDoesNotStartEnumeration()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ObjectStorageUploadPathMapper.MapAsync(
            ["C:/does-not-exist/private-file"], "", cancellation.Token));
    }

    /// <summary>文件夹中的循环链接被拒绝，无法沿链接重复遍历。</summary>
    [Fact]
    public async Task UploadPaths_RejectRecursiveDirectoryLinks()
    {
        using var area = new ObjectStorageDialogTestDirectory();
        string selected = Path.Combine(area.Root, "selected");
        Directory.CreateDirectory(selected);
        CreateDirectoryLink(Path.Combine(selected, "loop"), selected);
        var error = await Assert.ThrowsAsync<ObjectStorageException>(() =>
            ObjectStorageUploadPathMapper.MapAsync([selected], "", TestContext.Current.CancellationToken));
        Assert.Contains("重解析点", error.Message, StringComparison.Ordinal);
    }

    /// <summary>选择位于链接目录内的普通文件也不能绕过祖先路径校验。</summary>
    [Fact]
    public async Task UploadPaths_RejectSelectedFileWithLinkedAncestor()
    {
        using var area = new ObjectStorageDialogTestDirectory();
        string real = Path.Combine(area.Root, "real");
        string alias = Path.Combine(area.Root, "alias");
        Directory.CreateDirectory(real);
        await File.WriteAllTextAsync(Path.Combine(real, "item.txt"), "sample", TestContext.Current.CancellationToken);
        CreateDirectoryLink(alias, real);
        await Assert.ThrowsAsync<ObjectStorageException>(() =>
            ObjectStorageUploadPathMapper.MapAsync([Path.Combine(alias, "item.txt")], "", TestContext.Current.CancellationToken));
    }

    /// <summary>受限环境可能禁止创建测试链接，此时跳过需要操作系统权限的用例。</summary>
    private static void CreateDirectoryLink(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); }
        catch (UnauthorizedAccessException) { Assert.Skip("当前环境不允许建立测试符号链接。"); }
        catch (IOException) { Assert.Skip("当前文件系统不支持建立测试符号链接。"); }
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
    }
}
