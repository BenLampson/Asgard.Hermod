using Asgard.Hermod.UI.Views.Dialogs;
using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Services;

/// <summary>将后台 MCP 请求排到桌面线程；取消或关闭窗口一律拒绝。</summary>
public sealed class WindowsMcpApprovalService(Func<Window?> owner) : IMcpApprovalService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    /// <summary>显示边界可在离屏测试中替换。</summary>
    internal Action<Window> PresentWindow { get; init; } = WindowActivation.Show;

    /// <inheritdoc />
    public async Task<McpApprovalResult> ConfirmAsync(McpApprovalRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await _dispatcher.InvokeAsync(() => ShowAsync(request, cancellationToken)).Task.Unwrap().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<McpApprovalResult> ShowAsync(McpApprovalRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var model = new McpApprovalViewModel(request);
        var window = new McpApprovalWindow(model);
        if (owner() is { IsVisible: true } parent) window.Owner = parent;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        using var registration = token.Register(() =>
        {
            if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(() => { if (window.IsVisible) window.Close(); });
        });
        token.ThrowIfCancellationRequested();
        PresentWindow(window);
        await closed.Task;
        token.ThrowIfCancellationRequested();
        return new(window.Approved, window.Approved ? model.DownloadFolder : "");
    }
}
