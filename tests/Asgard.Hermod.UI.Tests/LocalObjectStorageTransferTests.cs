using System.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证用户文件传输的原子提交、取消、进度反馈和限量只读预览。</summary>
public sealed class LocalObjectStorageTransferTests
{
    /// <summary>下载只在完整流读取完成后替换旧文件；通知和客户端清理异常不抹掉已完成结果。</summary>
    [Fact]
    public async Task DownloadAsync_AtomicallyReplacesFileAndIsolatesProgressFailure()
    {
        using var environment = new LocalStorageTestEnvironment();
        Directory.CreateDirectory(environment.DirectoryPath);
        string destination = Path.Combine(environment.DirectoryPath, "download.txt");
        await File.WriteAllTextAsync(destination, "original", TestContext.Current.CancellationToken);
        byte[] bytes = Encoding.UTF8.GetBytes("complete downloaded content");
        var body = new ObjectStorageTestReadStream(bytes);
        var client = new FakeObjectStorageClient
        {
            ThrowOnDispose = true,
            Read = (_, key, maxBytes, _) =>
            {
                Assert.Equal("file.txt", key);
                Assert.Null(maxBytes);
                return Task.FromResult(new ObjectStorageReadResult(body, bytes.Length));
            },
        };
        var context = ObjectStorageTestServices.Create(client);
        var progress = new RecordingObjectStorageProgress { ThrowOnReport = true };

        await context.Service.DownloadAsync("saved-instance", new("test-bucket", "cn-hangzhou"), "file.txt", destination,
            progress, TestContext.Current.CancellationToken);

        Assert.Equal("complete downloaded content", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.True(body.IsDisposed);
        Assert.Equal(1, client.DisposeCount);
        Assert.Equal(bytes.Length, progress.Values.Last().TransferredBytes);
        Assert.Equal(bytes.Length, progress.Values.Last().TotalBytes);
        Assert.Single(Directory.EnumerateFiles(environment.DirectoryPath));
    }

    /// <summary>后台按已映射的目标位置创建普通子目录，便于目录内文件批量下载。</summary>
    [Fact]
    public async Task DownloadAsync_CreatesMappedSubdirectories()
    {
        using var environment = new LocalStorageTestEnvironment();
        Directory.CreateDirectory(environment.DirectoryPath);
        string destination = Path.Combine(environment.DirectoryPath, "docs", "nested", "download.txt");
        byte[] bytes = Encoding.UTF8.GetBytes("nested content");
        var client = new FakeObjectStorageClient
        {
            Read = (_, _, _, _) => Task.FromResult(new ObjectStorageReadResult(new MemoryStream(bytes), bytes.Length)),
        };
        var context = ObjectStorageTestServices.Create(client);

        await context.Service.DownloadAsync("saved-instance", new("test-bucket", "cn-hangzhou"), "docs/nested/download.txt", destination,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("nested content", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFiles(environment.DirectoryPath, "*", SearchOption.AllDirectories));
    }

    /// <summary>取消中断传输并清理临时文件，原有用户文件仍完整。</summary>
    [Fact]
    public async Task DownloadAsync_PreservesOriginalFileAndCleansTemporaryFileOnCancellation()
    {
        using var environment = new LocalStorageTestEnvironment();
        Directory.CreateDirectory(environment.DirectoryPath);
        string destination = Path.Combine(environment.DirectoryPath, "download.txt");
        await File.WriteAllTextAsync(destination, "original", TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var body = new ObjectStorageTestReadStream(new byte[128 * 1024], cancellation);
        var client = new FakeObjectStorageClient
        {
            Read = (_, _, _, _) => Task.FromResult(new ObjectStorageReadResult(body, 128 * 1024)),
        };
        var context = ObjectStorageTestServices.Create(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Service.DownloadAsync("saved-instance",
            new("test-bucket", "cn-hangzhou"), "file.txt", destination, cancellationToken: cancellation.Token));

        Assert.True(body.IsDisposed);
        Assert.Equal("original", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFiles(environment.DirectoryPath));
    }

    /// <summary>响应流短于 Content-Length 时不能将损坏内容提交到目标文件。</summary>
    [Fact]
    public async Task DownloadAsync_RejectsIncompleteBodyBeforeReplacingFile()
    {
        using var environment = new LocalStorageTestEnvironment();
        Directory.CreateDirectory(environment.DirectoryPath);
        string destination = Path.Combine(environment.DirectoryPath, "download.txt");
        await File.WriteAllTextAsync(destination, "original", TestContext.Current.CancellationToken);
        var client = new FakeObjectStorageClient
        {
            Read = (_, _, _, _) => Task.FromResult(new ObjectStorageReadResult(new MemoryStream([1, 2, 3]), 10)),
        };
        var context = ObjectStorageTestServices.Create(client);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => context.Service.DownloadAsync(
            "saved-instance", new("test-bucket", "cn-hangzhou"), "file.bin", destination,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("IncompleteDownload", error.ErrorCode);
        Assert.Equal("original", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFiles(environment.DirectoryPath));
    }

    /// <summary>正文流关闭失败发生在目标文件替换前，旧文件与安全错误保持完整。</summary>
    [Fact]
    public async Task DownloadAsync_ClosesRemoteStreamBeforeReplacingFile()
    {
        using var environment = new LocalStorageTestEnvironment();
        Directory.CreateDirectory(environment.DirectoryPath);
        string destination = Path.Combine(environment.DirectoryPath, "download.txt");
        await File.WriteAllTextAsync(destination, "original", TestContext.Current.CancellationToken);
        var body = new ObjectStorageTestReadStream(Encoding.UTF8.GetBytes("new content")) { ThrowOnDispose = true };
        var client = new FakeObjectStorageClient
        {
            Read = (_, _, _, _) => Task.FromResult(new ObjectStorageReadResult(body, 11)),
        };
        var context = ObjectStorageTestServices.Create(client);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => context.Service.DownloadAsync(
            "saved-instance", new("test-bucket", "cn-hangzhou"), "file.txt", destination,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(body.IsDisposed);
        Assert.Equal(11, body.BytesRead);
        Assert.Equal("StorageOperationFailed", error.ErrorCode);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private-resource-token", error.ToString());
        Assert.Equal("original", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFiles(environment.DirectoryPath));
    }

    /// <summary>上传使用用户文件的流和实际长度，覆盖授权不会被存在性检查替代。</summary>
    [Fact]
    public async Task UploadAsync_PassesReadableStreamAndExplicitOverwritePolicy()
    {
        using var environment = new LocalStorageTestEnvironment();
        Directory.CreateDirectory(environment.DirectoryPath);
        string source = Path.Combine(environment.DirectoryPath, "upload.txt");
        await File.WriteAllTextAsync(source, "uploaded content", TestContext.Current.CancellationToken);
        bool uploaded = false;
        var client = new FakeObjectStorageClient
        {
            ThrowOnDispose = true,
            Exists = (_, _, _) => throw new InvalidOperationException("存在性检查不能保证覆盖原子性。"),
            Upload = async (_, key, body, size, overwrite, _, cancellationToken) =>
            {
                Assert.Equal("docs/upload.txt", key);
                Assert.False(overwrite);
                Assert.Equal(body.Length, size);
                using var reader = new StreamReader(body, leaveOpen: true);
                Assert.Equal("uploaded content", await reader.ReadToEndAsync(cancellationToken));
                uploaded = true;
            },
        };
        var context = ObjectStorageTestServices.Create(client);

        await context.Service.UploadAsync("saved-instance", new("test-bucket", "cn-hangzhou"), "docs/upload.txt", source,
            overwrite: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(uploaded);
        Assert.Equal(1, client.DisposeCount);
        Assert.Equal("uploaded content", await File.ReadAllTextAsync(source, TestContext.Current.CancellationToken));
    }

    /// <summary>即使远端忽略 Range，预览也只读上限，并容忍截断处未完成的 UTF-8 字符。</summary>
    [Fact]
    public async Task GetObjectDetailsAsync_RestrictsPreviewBytesAndHandlesTruncatedUtf8()
    {
        const int limit = 256 * 1024;
        byte[] bytes = Encoding.UTF8.GetBytes(new string('x', limit - 1) + "é");
        var body = new ObjectStorageTestReadStream(bytes);
        var client = new FakeObjectStorageClient
        {
            Metadata = (_, key, _) => Task.FromResult(new ObjectStorageObjectDetails(key, bytes.Length, null,
                "Standard", "text/plain", "private")),
            Read = (_, _, maxBytes, _) =>
            {
                Assert.Equal(limit, maxBytes);
                return Task.FromResult(new ObjectStorageReadResult(body, bytes.Length));
            },
        };
        var context = ObjectStorageTestServices.Create(client);

        ObjectStorageObjectDetails details = await context.Service.GetObjectDetailsAsync("saved-instance", new("test-bucket", "cn-hangzhou"),
            "README.txt", TestContext.Current.CancellationToken);

        Assert.Equal(limit, body.BytesRead);
        Assert.True(body.IsDisposed);
        Assert.NotNull(details.PreviewText);
        Assert.Equal(limit - 1, details.PreviewText.Length);
        Assert.Contains("256KB", details.PreviewMessage);
        Assert.Equal("private", details.AccessControl);
    }

    /// <summary>二进制对象不下载正文，伪装成文本的无效 UTF-8 也不返回预览内容。</summary>
    [Fact]
    public async Task GetObjectDetailsAsync_RejectsBinaryAndInvalidUtf8Previews()
    {
        var client = new FakeObjectStorageClient
        {
            Metadata = (_, key, _) => Task.FromResult(new ObjectStorageObjectDetails(key, 1, null,
                "Standard", "image/png", "private")),
            Read = (_, _, _, _) => throw new InvalidOperationException("二进制预览不能读取正文。"),
        };
        var context = ObjectStorageTestServices.Create(client);
        ObjectStorageObjectDetails binary = await context.Service.GetObjectDetailsAsync("saved-instance", new("test-bucket", "cn-hangzhou"),
            "image.png", TestContext.Current.CancellationToken);
        Assert.Null(binary.PreviewText);
        client.Metadata = (_, key, _) => Task.FromResult(new ObjectStorageObjectDetails(key, 1, null,
            "Standard", "text/plain", "private"));
        client.Read = (_, _, _, _) => Task.FromResult(new ObjectStorageReadResult(new MemoryStream([255]), 1));

        ObjectStorageObjectDetails invalid = await context.Service.GetObjectDetailsAsync("saved-instance", new("test-bucket", "cn-hangzhou"),
            "bad.txt", TestContext.Current.CancellationToken);

        Assert.Null(invalid.PreviewText);
        Assert.Contains("UTF-8", invalid.PreviewMessage);
    }

    /// <summary>未分类第三方或本地异常都转为安全消息，绝不带原始异常或内层信息。</summary>
    [Fact]
    public async Task Operations_SanitizeUnexpectedProviderExceptions()
    {
        var client = new FakeObjectStorageClient
        {
            Exists = (_, _, _) => throw new InvalidOperationException("private-resource-token raw SDK request"),
        };
        var context = ObjectStorageTestServices.Create(client);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => context.Service.ObjectExistsAsync(
            "saved-instance", new("test-bucket", "cn-hangzhou"), "file.txt", TestContext.Current.CancellationToken));

        Assert.Equal("StorageOperationFailed", error.ErrorCode);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private-resource-token", error.ToString());
        Assert.DoesNotContain("raw SDK request", error.ToString());
    }

    /// <summary>删除提交成功后客户端清理失败，调用方仍得到成功而不会重复发起删除。</summary>
    [Fact]
    public async Task DeleteAsync_ReturnsCommittedResultWhenClientCleanupFails()
    {
        bool deleted = false;
        var client = new FakeObjectStorageClient
        {
            ThrowOnDispose = true,
            Delete = (_, keys, _) =>
            {
                Assert.Equal(["file.txt"], keys);
                deleted = true;
                return Task.CompletedTask;
            },
        };
        var context = ObjectStorageTestServices.Create(client);

        await context.Service.DeleteAsync("saved-instance", new("test-bucket", "cn-hangzhou"), new(["file.txt"]),
            TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.Equal(1, client.DisposeCount);
    }
}
