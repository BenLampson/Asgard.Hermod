using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>看图软件使用已经下载的原始正文，租约覆盖打开、重试和预览关闭。</summary>
public sealed class ObjectStorageImageOpeningTests
{
    /// <summary>未加载缩略图时，图片行的打开与预览也交给外部程序，不受内置解码限制。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImageRow_DownloadsForExternalProgramWithoutImageWindow(bool preview)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("image.png", size: FilePreviewPolicy.ImageLimit + 1);
        var files = new FileOpenTestService();
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage, files);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var row = model.Entries.Single(entry => entry.Name == "image.png");
        await (preview ? model.PreviewFileCommand : model.OpenFileCommand).ExecuteAsync(row);
        await Assert.Single(model.Transfers).Completion;
        Assert.Null(Assert.Single(service.DownloadLimits));
        var call = Assert.Single(files.Calls);
        Assert.False(call.Preview);
        Assert.True(File.Exists(call.Path));
        Assert.Null(model.ImagePreview);
    }

    /// <summary>详情、行打开和行预览重复使用同一份图片；未知扩展名根据解码格式关联软件。</summary>
    [Theory]
    [InlineData("image.png")]
    [InlineData("image.dat")]
    public async Task Open_ReusesOriginalDownloadAndProtectsExternalFile(string name)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service(name);
        var files = new FileOpenTestService();
        await using var model = ObjectStorageForcePreviewTestData.Model(service, ObjectStorageForcePreviewTestData.ConfirmingDialogs(), environment.Storage, files);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var row = model.Entries.Single(entry => entry.Name == name);
        await model.SelectEntryAsync(row, TestContext.Current.CancellationToken);
        Assert.False(model.OpenImagePreviewCommand.CanExecute(null));
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.Empty(files.Calls);
        await model.OpenImagePreviewCommand.ExecuteAsync();
        await model.OpenFileCommand.ExecuteAsync(row);
        await model.PreviewFileCommand.ExecuteAsync(row);
        Assert.Equal(3, files.Calls.Count);
        string path = files.Calls[0].Path;
        Assert.All(files.Calls, call => { Assert.Equal(path, call.Path); Assert.False(call.Preview); });
        Assert.Equal(".png", Path.GetExtension(path));
        Assert.Equal(ObjectStorageForcePreviewTestData.ImageBytes(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Single(service.DownloadLimits);
        Assert.Empty(model.Transfers);
        await model.SelectEntryAsync(model.Entries.Single(entry => entry.Name == "other.dat"), TestContext.Current.CancellationToken);
        Assert.False(model.OpenImagePreviewCommand.CanExecute(null));
        await model.CloseAsync();
        Assert.True(File.Exists(path));
        Assert.Equal(1, (await environment.Storage.InspectAsync(TestContext.Current.CancellationToken)).Usage.Single(usage => usage.Category == TemporaryFileCategory.Cache).FileCount);
    }

    /// <summary>打开失败显示安全错误，保留当前预览供原路径重试。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOpen_AllowsRetryOrCloseWithoutAnotherDownload(bool retry)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("image.png");
        var files = new FileOpenTestService { Fail = true };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage, files);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(entry => entry.Name == "image.png"), TestContext.Current.CancellationToken);
        await model.ForcePreviewCommand.ExecuteAsync();
        await model.OpenImagePreviewCommand.ExecuteAsync();
        Assert.Equal(Text.Get("FileOpenFailed"), model.ActionError);
        Assert.True(model.HasImagePreview);
        Assert.True(model.OpenImagePreviewCommand.CanExecute(null));
        if (!retry)
        {
            await model.CloseAsync();
            Assert.False(File.Exists(Assert.Single(files.Calls).Path));
            return;
        }
        files.Fail = false;
        await model.OpenImagePreviewCommand.ExecuteAsync();
        Assert.Empty(model.ActionError);
        Assert.Equal(files.Calls[0].Path, files.Calls[1].Path);
        Assert.Single(service.DownloadLimits);
    }

    /// <summary>未交给外部软件的完整图片随预览生命周期清理。</summary>
    [Theory]
    [InlineData("selection")]
    [InlineData("details")]
    [InlineData("refresh")]
    [InlineData("navigation")]
    [InlineData("close")]
    public async Task PreviewReset_CleansUnopenedImage(string action)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("image.png");
        string? path = null;
        service.DownloadHandler = (_, destination, _, token) => { path = destination; return File.WriteAllBytesAsync(destination, ObjectStorageForcePreviewTestData.ImageBytes(), token); };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage, new FileOpenTestService());
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(entry => entry.Name == "image.png"), TestContext.Current.CancellationToken);
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.True(File.Exists(path));
        if (action == "selection") await model.SelectEntryAsync(model.Entries.Single(entry => entry.Name == "other.dat"), TestContext.Current.CancellationToken);
        if (action == "details") await model.SetDetailsOpenAsync(false, TestContext.Current.CancellationToken);
        if (action == "refresh") await model.RefreshAsync(TestContext.Current.CancellationToken);
        if (action == "navigation") await model.NavigateAsync(service.Buckets[0], "folder/", TestContext.Current.CancellationToken);
        if (action == "close") await model.CloseAsync();
        Assert.False(File.Exists(path));
        Assert.False(model.OpenImagePreviewCommand.CanExecute(null));
    }

    /// <summary>打开期间关闭页签会取消启动并等待租约归还，不提前删除看图软件正在读取的文件。</summary>
    [Fact]
    public async Task Close_WaitsForPendingOpenAndCleansCancelledFile()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("image.png");
        var started = new TaskCompletionSource<(string Path, CancellationToken Token)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var files = new FileOpenTestService
        {
            OpenHandler = async (path, token) => { started.SetResult((path, token)); await finish.Task; token.ThrowIfCancellationRequested(); },
        };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage, files);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(entry => entry.Name == "image.png"), TestContext.Current.CancellationToken);
        await model.ForcePreviewCommand.ExecuteAsync();
        Task open = model.OpenImagePreviewCommand.ExecuteAsync();
        var pending = await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Task close = model.CloseAsync();
        Assert.False(close.IsCompleted);
        Assert.True(pending.Token.IsCancellationRequested);
        Assert.True(File.Exists(pending.Path));
        finish.SetResult();
        await Task.WhenAll(open, close).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(File.Exists(pending.Path));
    }
}
