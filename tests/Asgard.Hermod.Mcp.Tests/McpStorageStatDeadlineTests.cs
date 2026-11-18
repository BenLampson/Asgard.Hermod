using Asgard.Hermod.ObjectStorage.Abstractions.Clients;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpStorageStatDeadlineTests
{
    private static readonly McpTarget Storage = new("oss", "Objects", "storage", "v1");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredOrCancelledConfirmationCannotOpenStorageOrRememberALateDecision(bool clientCancels)
    {
        var clock = new RequestClock();
        var decision = new TaskCompletionSource<McpApprovalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var approval = ProtocolStub<IMcpApprovalService>.For((_, _) => decision.Task);
        using var rig = new McpTestRig([Storage], defaultAccess: true, approval: approval, clock: clock);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(McpTestRig.Token);
        var session = rig.Session;
        var pending = Stat(rig, session, caller.Token);
        Assert.False(pending.IsCompleted);
        if (clientCancels)
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        else
        {
            clock.ExpireAfter(30);
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5), McpTestRig.Token);
            Assert.True(result.IsError);
            Assert.Equal(Text.Get("McpApprovalTimedOut"), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        }
        decision.SetResult(new(true));
        Assert.False(session.HasApproval(McpDefaultAccess.Scope(Storage)));
        Assert.Equal(0, rig.Store.Opened);
    }

    [Fact]
    public async Task ApprovedQueryHasItsOwnTimeoutWithoutAProgressToken()
    {
        var clock = new RequestClock();
        var response = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = ProtocolStub<IObjectStorageClient>.For((_, args) => response.Task.WaitAsync((CancellationToken)args[2]!));
        var approval = ProtocolStub<IMcpApprovalService>.For((_, _) => Task.FromResult(new McpApprovalResult(true)));
        using var rig = new McpTestRig([Storage], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client),
            defaultAccess: true, approval: approval, clock: clock);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var pending = Stat(rig, rig.Session, McpTestRig.Token);
        Assert.Equal(1, rig.Store.Opened);
        clock.ExpireAfter(20);
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5), McpTestRig.Token);
        Assert.True(result.IsError);
        Assert.Equal(Text.Get("McpStorageStatTimedOut"), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    private static Task<CallToolResult> Stat(McpTestRig rig, McpSessionAccess session, CancellationToken token) =>
        rig.Tools.InvokeAsync(session, McpToolCatalog.StorageStat, McpTestRig.Inputs(McpDefaultAccess.InstanceGrant(session, Storage),
            ("bucket", "exports"), ("key", "words.json")), rig.Cursors, token);

    /// <summary>测试中每次只有一个请求计时器，按当前阶段的期限显式触发；周期清理使用普通计时器。</summary>
    private sealed class RequestClock : TimeProvider, ITimer
    {
        private Action? _fire;
        private TimeSpan _due;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan) return base.CreateTimer(callback, state, dueTime, period);
            _fire = () => callback(state);
            _due = dueTime;
            return this;
        }
        public bool Change(TimeSpan dueTime, TimeSpan period) { _due = dueTime; return true; }
        internal void ExpireAfter(int seconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(seconds), _due);
            Assert.NotNull(_fire);
            _fire();
        }
        public void Dispose() => _fire = null;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
