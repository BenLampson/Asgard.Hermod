using Asgard.Hermod.Lifetime;

namespace Asgard.Hermod.UI.Tests;

/// <summary>不发起实际注销，验证会话结束同步边界的有界异步清理。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SessionEndingCleanupTests(WpfTestFixture fixture)
{
    /// <summary>清理期间 Dispatcher 可用，完成后返回并允许系统会话结束。</summary>
    [Fact]
    public Task Cleanup_PumpsDispatcherAndCompletesWithinBudget() => fixture.RunAsync(() =>
    {
        bool cleaned = false;
        var args = new System.ComponentModel.CancelEventArgs(true);
        Assert.True(SessionEndingCleanup.Handle(args, async () =>
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => cleaned = true, DispatcherPriority.Background);
        }, TimeSpan.FromSeconds(1)));
        Assert.True(cleaned);
        Assert.False(args.Cancel);
        return Task.CompletedTask;
    });

    /// <summary>挂起清理不得无限阻断系统，也不设置 Cancel 来否决 Windows 注销。</summary>
    [Fact]
    public Task Cleanup_RespectsBudgetWhenServiceDoesNotFinish() => fixture.RunAsync(async () =>
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var args = new System.ComponentModel.CancelEventArgs();
        Assert.False(SessionEndingCleanup.Handle(args, () => pending.Task, TimeSpan.FromMilliseconds(80)));
        Assert.False(args.Cancel);
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(1));
        pending.SetResult(); await WpfTestFixture.DrainDispatcherAsync();
    });
}
