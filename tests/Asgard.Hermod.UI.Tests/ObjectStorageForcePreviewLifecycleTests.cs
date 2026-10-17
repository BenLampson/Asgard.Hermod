using Asgard.Hermod.ObjectStorage.UI.Models;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>选择与页面生命周期隔离迟到响应，取消下载不会影响其他操作。</summary>
public sealed class ObjectStorageForcePreviewLifecycleTests
{
    /// <summary>类型确认期间切换对象或关闭页面，迟到确认不下载旧对象。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateConfirmation_DoesNotDownloadObsoleteTarget(bool close)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service();
        var confirmation = new TaskCompletionSource<ObjectStoragePreviewType?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new ObjectStorageTestDialogs { ForcePreviewHandler = (_, _, _) => confirmation.Task };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, dialogs, environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.dat"), TestContext.Current.CancellationToken);
        Task read = model.ForcePreviewCommand.ExecuteAsync();
        if (close) await model.CloseAsync();
        else await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "other.dat"), TestContext.Current.CancellationToken);
        confirmation.SetResult(ObjectStoragePreviewType.Image);
        await read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Empty(service.DownloadLimits);
        Assert.Null(model.ImagePreview);
    }

    /// <summary>服务即使忽略取消，切换、关闭详情、刷新、目录导航和页签关闭均不显示旧图片。</summary>
    [Theory]
    [InlineData("selection")]
    [InlineData("details")]
    [InlineData("refresh")]
    [InlineData("navigation")]
    [InlineData("close")]
    public async Task ObsoleteDownload_IsCancelledIgnoredAndCleaned(string action)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service();
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken readToken = default;
        service.DownloadHandler = async (_, path, _, token) =>
        {
            readToken = token;
            started.SetResult(path);
            await finish.Task;
            await File.WriteAllBytesAsync(path, ObjectStorageForcePreviewTestData.ImageBytes(), TestContext.Current.CancellationToken);
        };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, ObjectStorageForcePreviewTestData.ConfirmingDialogs(), environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.dat"), TestContext.Current.CancellationToken);
        Task read = model.ForcePreviewCommand.ExecuteAsync();
        string path = await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(model.IsImagePreviewLoading);
        Task close = Task.CompletedTask;
        if (action == "selection") await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "other.dat"), TestContext.Current.CancellationToken);
        if (action == "details") model.CloseDetailsCommand.Execute(null);
        if (action == "refresh") await model.RefreshAsync(TestContext.Current.CancellationToken);
        if (action == "navigation") await model.NavigateAsync(service.Buckets[0], "folder/", TestContext.Current.CancellationToken);
        if (action == "close") close = model.CloseAsync();
        Assert.True(readToken.IsCancellationRequested);
        finish.SetResult();
        await Task.WhenAll(read, close).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Null(model.ImagePreview);
        Assert.False(model.IsImagePreviewLoading);
        Assert.False(File.Exists(path));
        if (action == "selection") Assert.Equal("other.dat", model.DetailName);
    }

    /// <summary>用户主动取消只作用于预览，释放半文件并说明已经读取的流量仍可能计费。</summary>
    [Fact]
    public async Task CancelLoading_CleansPartialFileAndAllowsRetry()
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service();
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DownloadHandler = async (_, path, _, token) =>
        {
            await File.WriteAllTextAsync(path, "partial", token);
            started.SetResult(path);
            await Task.Delay(Timeout.Infinite, token);
        };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, ObjectStorageForcePreviewTestData.ConfirmingDialogs(), environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.dat"), TestContext.Current.CancellationToken);
        Task read = model.ForcePreviewCommand.ExecuteAsync();
        string path = await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(model.CancelImagePreviewCommand.CanExecute(null));
        model.CancelImagePreviewCommand.Execute(null);
        await read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(Text.Get("ForcePreviewCancelled"), model.PreviewMessage);
        Assert.False(File.Exists(path));
        Assert.True(model.ForcePreviewCommand.CanExecute(null));
        Assert.True(model.DownloadCommand.CanExecute(null));
        Assert.False(model.CancelImagePreviewCommand.CanExecute(null));
    }
}
