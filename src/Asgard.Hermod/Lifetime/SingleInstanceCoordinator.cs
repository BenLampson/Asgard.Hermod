using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Asgard.Hermod.Lifetime;

/// <summary>在 DI 和 SQLite 前竞争进程所有权，使用受限管道合并启动中的激活请求。</summary>
public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private readonly InstanceLease _lease;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listener;
    private readonly object _gate = new();
    private Action? _activate;
    private bool _pending;
    private bool _exiting;
    private Task? _disposeTask;
    private readonly string _user = WindowsIdentity.GetCurrent().User!.Value;
    private readonly int _session = Process.GetCurrentProcess().SessionId;

    private SingleInstanceCoordinator(InstanceLease lease, string pipe)
    {
        _lease = lease;
        // 立即创建首个服务端，第二次启动无需等待数据库或 UI 初始化。
        _listener = ListenAsync(pipe);
    }

    /// <summary>默认标识固定；独立测试进程可传入隔离标识，不接触用户应用配置。</summary>
    public static async Task<SingleInstanceCoordinator?> AcquireOrActivateAsync(string applicationId = "Asgard.Hermod",
        CancellationToken cancellationToken = default)
    {
        string scope = applicationId + ":" + WindowsIdentity.GetCurrent().User!.Value + ":" + Process.GetCurrentProcess().SessionId;
        string name = "Hermod." + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(scope)));
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(45))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lease = new InstanceLease(name);
            if (lease.Acquired)
            {
                try { return new SingleInstanceCoordinator(lease, name); }
                catch { lease.Dispose(); throw; }
            }
            lease.Dispose();
            try
            {
                using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Impersonation);
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(TimeSpan.FromMilliseconds(700));
                await pipe.ConnectAsync(attempt.Token).ConfigureAwait(false);
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint processId)) continue;
                using var server = Process.GetProcessById((int)processId);
                if (server.SessionId != Process.GetCurrentProcess().SessionId) continue;
                AllowSetForegroundWindow(processId);
                await pipe.WriteAsync(new byte[] { 1 }, attempt.Token).ConfigureAwait(false);
                byte[] response = new byte[1];
                await pipe.ReadExactlyAsync(response, attempt.Token).ConfigureAwait(false);
                if (response[0] == 1) return null;
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { cancellationToken.ThrowIfCancellationRequested(); }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        throw Text.Exception("InstanceActivationFailed");
    }

    /// <summary>主窗体就绪后排入 UI Dispatcher；启动中收到的激活不会丢失。</summary>
    public void SetActivationHandler(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        lock (_gate)
        {
            _activate = activate;
            if (_pending && !_exiting) { _pending = false; activate(); }
        }
    }

    /// <summary>所有窗口批准退出后拒绝新激活，让二次启动等待原实例完成释放。</summary>
    public void BeginExit() { lock (_gate) _exiting = true; }

    /// <summary>每个连接限定用户 SID、进程登录会话及固定请求字节，不接受任意命令。</summary>
    private async Task ListenAsync(string name)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                byte[] request = new byte[1];
                await pipe.ReadExactlyAsync(request, timeout.Token).ConfigureAwait(false);
                bool userMatches = false;
                pipe.RunAsClient(() => userMatches = WindowsIdentity.GetCurrent().User?.Value == _user);
                bool accepted = false;
                if (userMatches && request[0] == 1 && GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid))
                {
                    using var client = Process.GetProcessById((int)pid);
                    if (client.SessionId == _session)
                        lock (_gate)
                        {
                            accepted = !_exiting;
                            if (accepted)
                            {
                                if (_activate is null) _pending = true;
                                else _activate();
                            }
                        }
                }
                await pipe.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                if (!_stop.IsCancellationRequested) await Task.Delay(30).ConfigureAwait(false);
            }
        }
    }

    /// <summary>先关闭激活通道并等待监听结束，最后释放进程所有权。</summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    /// <summary>关闭入口幂等，异常启动与正常退出不会重复释放内核句柄。</summary>
    private async Task DisposeCoreAsync()
    {
        BeginExit();
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await _listener.ConfigureAwait(false); }
        finally { _stop.Dispose(); _lease.Dispose(); }
    }

    /// <summary>校验 IPC 客户端与服务端均来自当前登录会话。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    /// <summary>读取原实例进程，用于 Windows 前台权限转交。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    /// <summary>二次启动进程把自己取得的前台权限授予原实例。</summary>
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
