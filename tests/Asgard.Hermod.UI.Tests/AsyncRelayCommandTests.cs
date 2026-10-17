using Asgard.Hermod.UI.Common;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证异步命令的启用状态与实际串行执行保护可以独立配置。</summary>
public sealed class AsyncRelayCommandTests
{
    /// <summary>默认行为继续禁用执行中的命令，结束后恢复并允许下一次执行。</summary>
    [Fact]
    public async Task Default_DisablesWhileRunningAndAllowsNextExecution()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var command = new AsyncRelayCommand(_ => { calls++; return completed.Task; });

        Task execution = command.ExecuteAsync();
        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, calls);

        completed.SetResult();
        await execution;
        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(2, calls);
    }

    /// <summary>保持启用只影响外观；仍拒绝重复执行，并遵守显式启用条件。</summary>
    [Fact]
    public async Task KeepEnabledWhileRunning_RejectsReentryAndHonorsPredicate()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        bool allowed = true;
        var command = new AsyncRelayCommand(_ => { calls++; return completed.Task; },
            _ => allowed, disableWhileRunning: false);

        Task execution = command.ExecuteAsync();
        Assert.True(command.IsRunning);
        Assert.True(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, calls);

        completed.SetResult();
        await execution;
        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
        allowed = false;
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, calls);
        allowed = true;
        await command.ExecuteAsync();
        Assert.Equal(2, calls);
    }
}
