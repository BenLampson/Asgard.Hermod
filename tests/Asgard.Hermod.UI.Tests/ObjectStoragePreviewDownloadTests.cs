namespace Asgard.Hermod.UI.Tests;

/// <summary>预览下载限制按实际正文计数，不能仅信任 Content-Length。</summary>
public sealed class ObjectStoragePreviewDownloadTests
{
    /// <summary>服务端声明过大或隐藏真实长度时均停止下载，原文件不被破坏。</summary>
    [Theory]
    [InlineData(100)]
    [InlineData(-1)]
    public async Task Download_RejectsOverLimitAndPreservesDestination(long declaredLength)
    {
        using var environment = new LocalStorageTestEnvironment();
        Directory.CreateDirectory(environment.DirectoryPath);
        string destination = Path.Combine(environment.DirectoryPath, "file.txt");
        await File.WriteAllTextAsync(destination, "original", TestContext.Current.CancellationToken);
        var client = new FakeObjectStorageClient
        {
            Read = (_, _, _, _) => Task.FromResult(new ObjectStorageReadResult(new MemoryStream(new byte[100]), declaredLength))
        };
        var context = ObjectStorageTestServices.Create(client);
        var error = await Assert.ThrowsAsync<ObjectStorageException>(() => context.Service.DownloadAsync("saved-instance",
            new("test-bucket", "cn-hangzhou"), "file.txt", destination, cancellationToken: TestContext.Current.CancellationToken, maxBytes: 10));
        Assert.Equal("PreviewLimitExceeded", error.ErrorCode);
        Assert.Equal("original", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFiles(environment.DirectoryPath));
    }
}
