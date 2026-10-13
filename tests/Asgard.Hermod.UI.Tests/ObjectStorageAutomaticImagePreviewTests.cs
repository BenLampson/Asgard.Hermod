using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>已识别图片的自动预览、直接重试以及后台读取生命周期。</summary>
public sealed class ObjectStorageAutomaticImagePreviewTests
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
        Assert.False(model.HasImagePreview);
        Assert.Contains(Text.Get("ForcePreviewDecodeFailed"), model.PreviewMessage, StringComparison.Ordinal);
        Assert.Equal(Text.Get("ForcePreviewRetryImage"), model.ForcePreviewLabel);
        Assert.False(model.ShowForcePreviewIntroduction);
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.True(model.HasImagePreview);
        Assert.Equal(2, attempts);
        Assert.Empty(model.ActionError);
    }

    /// <summary>图片切换后允许加载新对象，但关闭页签仍等待忽略取消的旧读取释放临时文件。</summary>
    [Fact]
    public async Task Close_WaitsForBothCurrentAndObsoleteAutomaticDownloads()
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
        Task old = model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.png"), TestContext.Current.CancellationToken);
        string oldPath = await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "other.dat"), TestContext.Current.CancellationToken);
        Assert.True(oldToken.IsCancellationRequested);
        Assert.True(model.HasImagePreview);
        Assert.Equal("other.dat", model.DetailName);
        Task close = model.CloseAsync();
        Assert.False(close.IsCompleted);
        finish.SetResult();
        await Task.WhenAll(old, close).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(model.HasImagePreview);
        Assert.False(File.Exists(oldPath));
    }

    /// <summary>详情隐藏期间选择其他文件不会读元数据或正文，展开才预览当前图片。</summary>
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
        Assert.True(model.HasImagePreview);
        Assert.Single(service.DetailsCalls);
        Assert.Single(service.DownloadLimits);
        model.CloseDetailsCommand.Execute(null);
        Assert.False(model.IsDetailsOpen);
        Assert.Same(image, model.SelectedEntry);
        Assert.False(model.HasImagePreview);
    }
}
