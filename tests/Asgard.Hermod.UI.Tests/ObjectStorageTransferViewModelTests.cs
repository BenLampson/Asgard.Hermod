namespace Asgard.Hermod.UI.Tests;

/// <summary>验证传输任务取消、进度、失败重试与晚到通知。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageTransferViewModelTests
{
    private readonly WpfTestFixture _fixture;
    /// <summary>进度测试复用实际 Dispatcher 上下文，避免依赖线程池调度时机。</summary>
    public ObjectStorageTransferViewModelTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>真实进度更新到界面，取消后可重试且失败任务不会被替换。</summary>
    [Fact]
    public Task ProgressAndCancel_CanRetrySameTask() => _fixture.RunAsync(async () =>
    {
        int runs = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IProgress<ObjectStorageTransferProgress>? savedProgress = null;
        using var task = new ObjectStorageTransferViewModel("releases/a.txt", true, 100, async (_, progress, token) =>
        {
            if (++runs > 1) return;
            savedProgress = progress;
            progress.Report(new(60, 100));
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        }, TestContext.Current.CancellationToken);
        var pending = task.RunAsync();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.Equal(60, task.Percent);
        Assert.True(task.IsRunning);
        Assert.True(task.CancelCommand.CanExecute(null));
        task.CancelCommand.Execute(null);
        await pending;
        Assert.Equal("已取消", task.State);
        Assert.True(task.CanRetry);
        savedProgress!.Report(new(99, 100));
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.Equal(60, task.Percent);

        await task.RetryCommand.ExecuteAsync();
        Assert.True(task.IsCompleted);
        Assert.Equal(100, task.Percent);
        Assert.Equal(2, runs);
    });

    /// <summary>未知传输异常不暴露原始路径或访问凭据，重试成功清空错误。</summary>
    [Fact]
    public async Task Failure_KeepsSafeMessageAndRetryState()
    {
        int runs = 0;
        using var task = new ObjectStorageTransferViewModel("a.txt", false, 10, (_, _, _) =>
            ++runs == 1 ? throw new InvalidOperationException("private-local-path-and-secret") : Task.CompletedTask,
            TestContext.Current.CancellationToken);
        await task.RunAsync();
        Assert.True(task.IsFailed);
        Assert.DoesNotContain("private-local", task.Error, StringComparison.Ordinal);
        Assert.True(task.RetryCommand.CanExecute(null));
        await task.RetryCommand.ExecuteAsync();
        Assert.True(task.IsCompleted);
        Assert.Empty(task.Error);
    }
}
