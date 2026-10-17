namespace Asgard.Hermod.UI.Tests;

/// <summary>打开和预览使用真实缓存文件，验证失败、关闭与重试的生命周期。</summary>
public sealed class ObjectStorageFileOpeningTests
{
    /// <summary>打开所选文件只下载该键，完成之后才打开；预览关闭即清理，外部打开保留缓存。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_DownloadsBeforeOpeningAndKeepsCapturedTarget(bool preview)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var files = new FileOpenTestService();
        var service = CreateService();
        string? downloaded = null;
        service.DownloadHandler = async (key, path, _, token) =>
        {
            Assert.Equal("docs/a.txt", key);
            Assert.Empty(files.Calls);
            downloaded = path;
            await File.WriteAllTextAsync(path, "complete file", token);
        };
        await using var model = CreateModel(service, files, environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var row = model.Entries.Single(entry => entry.Key == "docs/a.txt");
        await (preview ? model.PreviewFileCommand : model.OpenFileCommand).ExecuteAsync(row);
        await Assert.Single(model.Transfers).Completion;
        var call = Assert.Single(files.Calls);
        Assert.Equal(downloaded, call.Path);
        Assert.Equal(preview, call.Preview);
        Assert.Equal(!preview, File.Exists(call.Path));
        Assert.True(model.Transfers[0].IsCompleted);
    }

    /// <summary>下载失败不打开，重试创建独立路径，不复用残留半文件。</summary>
    [Fact]
    public async Task Failure_CleansPartialCacheAndRetryUsesFreshPath()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var files = new FileOpenTestService();
        var service = CreateService();
        var paths = new List<string>();
        service.DownloadHandler = async (_, path, _, token) =>
        {
            paths.Add(path);
            await File.WriteAllTextAsync(path, "body", token);
            if (paths.Count == 1) throw new IOException("private-access-key");
        };
        await using var model = CreateModel(service, files, environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.OpenEntryAsync(model.Entries.Single(entry => entry.Key == "docs/a.txt"), TestContext.Current.CancellationToken);
        var transfer = Assert.Single(model.Transfers);
        await transfer.Completion;
        Assert.True(transfer.IsFailed);
        Assert.Empty(files.Calls);
        Assert.False(File.Exists(paths[0]));
        Assert.DoesNotContain("private-access-key", transfer.Error);
        await transfer.RetryCommand.ExecuteAsync();
        Assert.True(transfer.IsCompleted);
        Assert.NotEqual(paths[0], paths[1]);
        Assert.Equal(paths[1], Assert.Single(files.Calls).Path);
    }

    /// <summary>即使下载服务忽略取消，页面关闭后也不得打开迟到文件。</summary>
    [Fact]
    public async Task Close_DoesNotOpenLateDownloadAndCleansCache()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var files = new FileOpenTestService();
        var service = CreateService();
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DownloadHandler = async (_, path, _, _) =>
        {
            started.SetResult(path);
            await finish.Task;
            await File.WriteAllTextAsync(path, "late", TestContext.Current.CancellationToken);
        };
        await using var model = CreateModel(service, files, environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.OpenFileCommand.ExecuteAsync(model.Entries.Single(entry => entry.Key == "docs/a.txt"));
        string path = await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Task close = model.CloseAsync();
        finish.SetResult();
        await close.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Empty(files.Calls);
        Assert.False(File.Exists(path));
        Assert.False(model.OpenFileCommand.CanExecute(null));
    }

    /// <summary>文件夹仍然导航而不调用文件服务；不支持的格式保留下载入口。</summary>
    [Fact]
    public async Task Directory_NavigatesAndUnsupportedPreviewStaysDisabled()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var files = new FileOpenTestService();
        await using var model = CreateModel(CreateService(), files, environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(model.PreviewFileCommand.CanExecute(model.Entries.Single(entry => entry.Key == "archive.zip")));
        await model.OpenEntryAsync(model.Entries.Single(entry => entry.IsDirectory), TestContext.Current.CancellationToken);
        Assert.Equal("docs/", model.Prefix);
        Assert.Empty(files.Calls);
        Assert.Empty(model.Transfers);
    }

    /// <summary>固定数据不依赖网络、机器账户或界面显示语言。</summary>
    private static ObjectStorageTestService CreateService() => new()
    {
        ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage(
            [new("docs/a.txt", false, 13), new("archive.zip", false, 50), new("docs/", true)]))
    };
    /// <summary>显式提供文件打开边界，不启动真实程序。</summary>
    private static ObjectStorageWorkspaceViewModel CreateModel(ObjectStorageTestService service, IFileOpenService files, ITemporaryFileStorage temporaryFiles) =>
        new(new InstanceItemViewModel(new InstanceSummary("oss", "oss", "test", "cloud", [], TypeId: "storage", ProviderId: "aliyun-oss")),
            service, new ObjectStorageTestDialogs(), files: files, temporaryFiles: temporaryFiles);
}
