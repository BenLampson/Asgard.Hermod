using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Asgard.Hermod.Mcp;

/// <summary>桌面宿主管理的当前用户 IPC，关闭时中止外部访问和未完成的调用。</summary>
public sealed class McpDesktopServer : IDisposable, IAsyncDisposable
{
    private const int Connections = 4;
    private readonly McpAccessService _access;
    private readonly McpProtocolSession _protocol;
    private readonly IAccountService? _accounts;
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private Task? _listeners;
    private Task? _disposal;
    private bool _stopped;

    internal McpDesktopServer(McpAccessService access, McpProtocolSession protocol, IAccountService? accounts = null, string? name = null)
    {
        _access = access;
        _protocol = protocol;
        _accounts = accounts;
        _name = name ?? McpPipeEndpoint.Name;
        if (_accounts is not null) _accounts.AccountChanged += OnAccountChanged;
    }

    /// <summary>在本地工作区恢复后开始监听；默认策略会拒绝配对。</summary>
    public void Start()
    {
        lock (_gate)
            if (!_stopped && _listeners is null)
                _listeners = Task.WhenAll(Enumerable.Range(0, Connections).Select(_ => Task.Run(() => ListenAsync(_stop.Token))));
    }

    /// <summary>退出已获确认后立即停止接收任务，不等待窗口清理完成。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true;
            if (_accounts is not null) _accounts.AccountChanged -= OnAccountChanged;
            _access.Dispose();
            _stop.Cancel();
        }
    }

    private void OnAccountChanged(object? sender, EventArgs args) => _access.Disconnect();

    private async Task ListenAsync(CancellationToken stop)
    {
        var security = new PipeSecurity();
        security.SetOwner(McpPipeEndpoint.User);
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new(McpPipeEndpoint.User, PipeAccessRights.FullControl, AccessControlType.Allow));
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = NamedPipeServerStreamAcl.Create(_name, PipeDirection.InOut, Connections,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                if (!McpPipeEndpoint.IsLocalPeer(pipe, peerIsServer: false) || _access.Connect() is not { } access) continue;
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stop, access.Revoked);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                if (!await McpPipeEndpoint.AcceptAsync(pipe, access.Policy.Secret, timeout.Token).ConfigureAwait(false)) continue;
                _access.Check(access);
                using var interrupt = lifetime.Token.Register(pipe.Dispose);
                await _protocol.RunAsync(pipe, access, lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 配对失败、退出和客户端主动断开不展示 SDK 原文，也不影响其他连接。
                if (!stop.IsCancellationRequested) await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Stop();
        lock (_gate) return new(_disposal ??= ReleaseAsync());
    }

    private async Task ReleaseAsync()
    {
        if (_listeners is not null)
        {
            try { await _listeners.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception) { /* 防止无法取消的连接拖住桌面退出。 */ }
        }
        _stop.Dispose();
    }
}
