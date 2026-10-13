using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>图片仅在点击按钮后读取，已识别类型不弹窗，并保留取消与重试行为。</summary>
public sealed class ObjectStorageButtonImagePreviewTests
{
    /// <summary>已识别图片首次失败后直接重试，不能重新要求选择图片类型。</summary>
    [Fact]
    public async Task KnownImage_RetriesWithoutTypeDialog()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("image.png", "image/png");
        int attempts = 0;
        service.DownloadHandler = (_, path, _, token) => ++attempts == 1
            ? File.WriteAllTextAsync(path, "invalid-image", token)
            : File.WriteAllBytesAsync(path, ObjectStorageForcePreviewTestData.ImageBytes(), token);
        var dialogs = new ObjectStorageTestDialogs { ForcePreviewHandler = (_, _, _) => throw new InvalidOperationException("Unexpected type dialog") };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, dialogs, environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.png"), TestContext.Current.CancellationToken);
        Assert.Empty(service.DownloadLimits);
        Assert.Equal(Text.Get("ForcePreview"), model.ForcePreviewLabel);
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.False(model.HasImagePreview);
        Assert.Contains(Text.Get("ForcePreviewDecodeFailed"), model.PreviewMessage, StringComparison.Ordinal);
        Assert.Equal(Text.Get("ForcePreviewRetryImage"), model.ForcePreviewLabel);
        Assert.False(model.ShowForcePreviewIntroduction);
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.True(model.HasImagePreview);
        Assert.Equal(2, attempts);
        Assert.Empty(model.ActionError);
    }

    /// <summary>图片切换不读取新正文；关闭页签仍等待忽略取消的旧读取释放临时文件。</summary>
    [Fact]
    public async Task Close_WaitsForObsoleteButtonDownload()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("image.png", "image/png");
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        service.DownloadHandler = async (key, path, _, token) =>
        {
            if (key == "image.png")
            {
                oldToken = token; started.SetResult(path);
                await finish.Task;
            }
            await File.WriteAllBytesAsync(path, ObjectStorageForcePreviewTestData.ImageBytes(), TestContext.Current.CancellationToken);
        };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.png"), TestContext.Current.CancellationToken);
        Task old = model.ForcePreviewCommand.ExecuteAsync();
        string oldPath = await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "other.dat"), TestContext.Current.CancellationToken);
        Assert.True(oldToken.IsCancellationRequested);
        Assert.False(model.HasImagePreview);
        Assert.Single(service.DownloadLimits);
        Assert.Equal("other.dat", model.DetailName);
        Task close = model.CloseAsync();
        Assert.False(close.IsCompleted);
        finish.SetResult();
        await Task.WhenAll(old, close).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(model.HasImagePreview);
        Assert.False(File.Exists(oldPath));
    }

    /// <summary>隐藏期间不读取，展开仅加载元数据，用户点击后才读取图片。</summary>
    [Fact]
    public async Task HiddenDetails_PreservesSelectionAndSkipsReadsUntilReopened()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("image.png", "image/png");
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage);
        await model.SetDetailsOpenAsync(false, TestContext.Current.CancellationToken);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var image = model.Entries.Single(row => row.Name == "image.png");
        await model.SelectEntryAsync(image, TestContext.Current.CancellationToken);
        Assert.Same(image, model.SelectedEntry);
        Assert.Empty(service.DetailsCalls);
        Assert.Empty(service.DownloadLimits);
        Assert.False(model.ForcePreviewCommand.CanExecute(null));
        await model.ToggleDetailsCommand.ExecuteAsync();
        Assert.True(model.IsDetailsOpen);
        Assert.False(model.HasImagePreview);
        Assert.Single(service.DetailsCalls);
        Assert.Empty(service.DownloadLimits);
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.True(model.HasImagePreview);
        Assert.Single(service.DownloadLimits);
        model.CloseDetailsCommand.Execute(null);
        Assert.False(model.IsDetailsOpen);
        Assert.Same(image, model.SelectedEntry);
        Assert.False(model.HasImagePreview);
    }

    /// <summary>首次选择、刷新后重选及再次展开均不读取图片，避免无意产生流量。</summary>
    [Fact]
    public async Task SelectionAndRefresh_OnlyReadMetadataUntilButtonIsClicked()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("image.png", "image/png");
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.png"), TestContext.Current.CancellationToken);
        Assert.Empty(service.DownloadLimits);
        Assert.Equal(Text.Get("ForcePreviewClickToLoadImage"), model.PreviewMessage);
        await model.RefreshAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.png"), TestContext.Current.CancellationToken);
        Assert.Empty(service.DownloadLimits);
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.True(model.HasImagePreview);
        await model.SetDetailsOpenAsync(false, TestContext.Current.CancellationToken);
        await model.SetDetailsOpenAsync(true, TestContext.Current.CancellationToken);
        Assert.False(model.HasImagePreview);
        Assert.Single(service.DownloadLimits);
    }
}
