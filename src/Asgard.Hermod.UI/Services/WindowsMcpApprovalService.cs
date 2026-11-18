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
        try
        {
            return await _dispatcher.InvokeAsync(() => ShowAsync(request, cancellationToken), DispatcherPriority.Normal, cancellationToken)
                .Task.Unwrap().ConfigureAwait(false);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Text.Exception("McpApprovalUnavailable");
        }
        finally { _gate.Release(); }
    }

    private async Task<McpApprovalResult> ShowAsync(McpApprovalRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var model = new McpApprovalViewModel(request);
        var window = new McpApprovalWindow(model);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            if (owner() is { IsVisible: true, WindowState: not WindowState.Minimized } parent) window.Owner = parent;
            else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            token.ThrowIfCancellationRequested();
            PresentWindow(window);
            await closed.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            return new(window.Approved, window.Approved ? model.DownloadFolder : "");
        }
        finally { if (!closed.Task.IsCompleted) window.Close(); }
    }
}
