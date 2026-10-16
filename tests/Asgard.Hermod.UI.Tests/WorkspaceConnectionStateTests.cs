using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>共享连接状态支持取消、断线去重和显式重试，不依赖协议或桌面窗口。</summary>
public sealed class WorkspaceConnectionStateTests
{
    /// <summary>取消只发送一次，清理结束后关闭加载弹窗。</summary>
    [Fact]
    public void Cancel_WaitsForFinishAndCannotRepeat()
    {
        var state = new WorkspaceConnectionViewModel(); int cancellations = 0;
        state.Begin(() => cancellations++); state.CancelCommand.Execute(null);
        Assert.Equal(1, cancellations); Assert.True(state.IsOpen); Assert.True(state.IsCancelling);
        Assert.False(state.CancelCommand.CanExecute(null));
        state.Finish(); Assert.False(state.IsOpen); Assert.False(state.HasError);
    }
    /// <summary>断线弹窗与首次失败标题分开，关闭后同一轮失败不反复打断用户。</summary>
    [Fact]
    public void Disconnected_IsDismissibleAndCoalescesRepeatedFailures()
    {
        var state = new WorkspaceConnectionViewModel(); state.Disconnected("");
        Assert.True(state.IsDisconnected); Assert.False(state.IsOpen);
        Assert.Equal(Text.Get("WorkspaceConnectionInterrupted"), state.Title);
        Assert.Equal(Text.Get("WorkspaceConnectionInterruptedHint"), state.Detail);
        state.DismissCommand.Execute(null); state.Disconnected("ignored repeated failure");
        Assert.False(state.IsOpen); Assert.True(state.HasError);
        Assert.Equal(Text.Get("WorkspaceConnectionInterruptedHint"), state.Detail);
    }
    /// <summary>重连清除旧状态，失败后再显示本次经过脱敏的说明。</summary>
    [Fact]
    public void Retry_ReplacesOldFailureWithCurrentConnectionResult()
    {
        var state = new WorkspaceConnectionViewModel(); state.Disconnected("old failure"); state.Begin(() => { }, inlineRecovery: true);
        Assert.False(state.IsDisconnected); Assert.False(state.HasError); Assert.True(state.IsConnecting);
        state.Finish("safe failure"); Assert.False(state.IsOpen); Assert.False(state.IsDisconnected);
        Assert.Equal(Text.Get("WorkspaceConnectionFailed"), state.Title); Assert.Equal("safe failure", state.Detail);
    }
}
