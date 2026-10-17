namespace Asgard.Hermod.UI.Tests;

/// <summary>自动图片预览只作用于当前工作区与当前对象，所有读取使用假服务和受管缓存。</summary>
public sealed class ObjectStorageAutoImagePreviewTests
{
    [Fact]
    public async Task OptIn_RemainsEnabledAfterImageAndOptOutRestoresManualSelection()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("first.png", "image/png");
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var first = model.Entries.Single(row => row.Name == "first.png");
        await model.SelectEntryAsync(first, TestContext.Current.CancellationToken); Assert.Empty(service.DownloadLimits); Assert.False(model.AutoPreviewImages);
        await model.SelectEntryAsync(null, TestContext.Current.CancellationToken);
        model.AutoPreviewImages = true;
        await model.SelectEntryAsync(first, TestContext.Current.CancellationToken);
        Assert.True(model.HasImagePreview); Assert.True(model.AutoPreviewImages); Assert.Single(service.DownloadLimits);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "other.dat"), TestContext.Current.CancellationToken);
        Assert.True(model.HasImagePreview); Assert.Equal(2, service.DownloadLimits.Count);
        model.AutoPreviewImages = false;
        await model.SelectEntryAsync(first, TestContext.Current.CancellationToken);
        Assert.False(model.HasImagePreview); Assert.True(model.ShowForcePreviewActions); Assert.Equal(2, service.DownloadLimits.Count);
        await using var another = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage);
        Assert.False(another.AutoPreviewImages);
    }

    [Fact]
    public async Task NonImagesDirectoriesHiddenDetailsAndOversizedImagesNeverDownload()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service();
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage);
        model.AutoPreviewImages = true; await model.InitializeAsync(TestContext.Current.CancellationToken);
        foreach (var entry in model.Entries) await model.SelectEntryAsync(entry, TestContext.Current.CancellationToken);
        Assert.Empty(service.DownloadLimits);
        service.DetailsHandler = (_, _, key, _) => Task.FromResult(new ObjectStorageObjectDetails(key, FilePreviewPolicy.ImageLimit + 1, null, "", "image/png", ""));
        await model.SelectEntryAsync(model.Entries.First(row => !row.IsDirectory), TestContext.Current.CancellationToken);
        Assert.Empty(service.DownloadLimits); Assert.False(model.HasImagePreview);
        await model.SetDetailsOpenAsync(false, TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Last(row => !row.IsDirectory), TestContext.Current.CancellationToken);
        Assert.Empty(service.DownloadLimits);
    }

    [Fact]
    public async Task RapidSelection_CancelsAndSerializesOldProviderThatIgnoresCancellation()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("first.png", "image/png");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default; int concurrent = 0, maximum = 0;
        service.DownloadHandler = async (key, path, _, token) =>
        {
            maximum = Math.Max(maximum, ++concurrent);
            try
            {
                if (key == "first.png") { oldToken = token; started.SetResult(); await finish.Task; }
                await File.WriteAllBytesAsync(path, ObjectStorageForcePreviewTestData.ImageBytes(), TestContext.Current.CancellationToken);
            }
            finally { concurrent--; }
        };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage);
        model.AutoPreviewImages = true; await model.InitializeAsync(TestContext.Current.CancellationToken);
        Task old = model.SelectEntryAsync(model.Entries.Single(row => row.Name == "first.png"), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Task next = model.SelectEntryAsync(model.Entries.Single(row => row.Name == "other.dat"), TestContext.Current.CancellationToken);
        Assert.True(oldToken.IsCancellationRequested); Assert.Single(service.DownloadLimits);
        finish.SetResult(); await Task.WhenAll(old, next).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, maximum); Assert.Equal(2, service.DownloadLimits.Count);
        Assert.True(model.HasImagePreview); Assert.Equal("other.dat", model.DetailName);
    }

    [Fact]
    public async Task Failure_DoesNotAutomaticallyRetryAndToggleOffCancelsPendingRead()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("first.png", "image/png");
        service.DownloadHandler = (_, path, _, token) => File.WriteAllTextAsync(path, "invalid", token);
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage);
        model.AutoPreviewImages = true; await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "first.png"), TestContext.Current.CancellationToken);
        Assert.False(model.HasImagePreview); Assert.Single(service.DownloadLimits); Assert.True(model.ShowForcePreviewActions);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken pending = default;
        service.DownloadHandler = async (_, _, _, token) => { pending = token; started.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        Task read = model.SelectEntryAsync(model.Entries.Single(row => row.Name == "other.dat"), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); model.AutoPreviewImages = false;
        await read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(pending.IsCancellationRequested); Assert.False(model.IsImagePreviewLoading); Assert.False(model.HasImagePreview);
    }
}
