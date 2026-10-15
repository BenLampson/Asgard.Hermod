using Asgard.Hermod.UI.Models;
using Asgard.Hermod.UI.Views.Dialogs;
using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Tests;

/// <summary>重复启动通知在内存边界验证；不发送 IPC、不初始化数据库、不显示真实窗口。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class RepeatedLaunchNoticeTests(WpfTestFixture fixture)
{
    /// <summary>先激活现有实例，再给出信息提示，提示显示期间的重复请求合并。</summary>
    [Fact]
    public Task RepeatedRequestsActivateExistingWindowWithoutStackingNotices() => fixture.RunOffscreenAsync(() =>
    {
        var owner = new Window(); int activations = 0, prompts = 0; RepeatedLaunchNotice? notice = null;
        try
        {
            notice = new(owner, () => activations++, () => true, () => null, dialog =>
            {
                prompts++; Assert.Equal(MessageDialogKind.Information, dialog.Kind); Assert.True(activations > 0);
                notice!.Notify(); Assert.Equal(1, prompts);
            });
            notice.Notify(); Assert.Equal(2, activations); Assert.Equal(1, prompts);
        }
        finally { notice?.Dispose(); owner.Close(); }
        return Task.CompletedTask;
    });

    /// <summary>已有模态输入时只唤回输入窗口，关闭后才合并显示一次提示。</summary>
    [Fact]
    public Task ActiveModalInputIsPreservedAndNoticeWaitsForItsClose() => fixture.RunOffscreenAsync(async () =>
    {
        var owner = new Window(); var input = new TextBox { Text = "unsaved input" }; var modal = new Window { Content = input };
        Window? currentModal = modal; int activations = 0, prompts = 0;
        using var notice = new RepeatedLaunchNotice(owner, () => activations++, () => true, () => currentModal, _ => prompts++);
        try
        {
            notice.Notify(); notice.Notify(); Assert.Equal(2, activations); Assert.Equal(0, prompts); Assert.Equal("unsaved input", input.Text);
            currentModal = null; modal.Close(); await WpfTestFixture.DrainDispatcherAsync(); Assert.Equal(1, prompts);
        }
        finally { notice.Dispose(); modal.Close(); owner.Close(); }
    });

    /// <summary>应用退出和主窗体关闭后不能由迟到的激活请求重新打开提示。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ExitOrDisposalSuppressesNewNotices(bool exiting) => fixture.RunOffscreenAsync(() =>
    {
        var owner = new Window(); int activations = 0, prompts = 0;
        using var notice = new RepeatedLaunchNotice(owner, () => activations++, () => !exiting, () => null, _ => prompts++);
        try
        {
            if (!exiting) notice.Dispose();
            notice.Notify(); Assert.Equal(0, activations); Assert.Equal(0, prompts);
        }
        finally { owner.Close(); }
        return Task.CompletedTask;
    });

    /// <summary>等待业务输入期间退出会撤销延迟事件，不在输入结束后显示新窗口。</summary>
    [Fact]
    public Task DisposalCancelsDeferredNotice() => fixture.RunOffscreenAsync(async () =>
    {
        var owner = new Window(); var modal = new Window(); int prompts = 0;
        using var notice = new RepeatedLaunchNotice(owner, () => { }, () => true, () => modal, _ => prompts++);
        try { notice.Notify(); notice.Dispose(); modal.Close(); await WpfTestFixture.DrainDispatcherAsync(); Assert.Equal(0, prompts); }
        finally { modal.Close(); owner.Close(); }
    });
}
