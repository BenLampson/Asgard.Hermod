using System.Net;
using System.Net.Sockets;

namespace Asgard.Hermod.Ssh;

/// <summary>将单个本机 SDK 连接接入 TCP 代理；不监听外部接口，不解密 SSH 内容。</summary>
internal sealed class SshProxyBridge : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop;
    private readonly Task _relay;
    private TcpClient? _local;
    private TcpClient? _proxy;
    private Exception? _failure;

    /// <summary>监听随机本机端口，仅接受本次连接并立即关闭监听。</summary>
    internal SshProxyBridge(SshConnectionOptions options, CancellationToken cancellationToken)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start(1);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _relay = RelayAsync(options);
    }
    /// <summary>SDK 连接到这个本机端口，身份校验仍使用实际目标主机。</summary>
    internal int Port { get; }
    /// <summary>安全归类时使用代理阶段的失败，不向用户返回原文。</summary>
    internal Exception? Failure => Volatile.Read(ref _failure);

    /// <summary>建立代理后双向搬运字节，任一端结束都回收另一端。</summary>
    private async Task RelayAsync(SshConnectionOptions options)
    {
        try
        {
            _local = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            _listener.Stop(); _local.NoDelay = true;
            _proxy = new TcpClient { NoDelay = true };
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                await _proxy.ConnectAsync(options.ProxyHost, options.ProxyPort, timeout.Token).ConfigureAwait(false);
                await SshProxyProtocol.ConnectAsync(_proxy.GetStream(), options, timeout.Token).ConfigureAwait(false);
            }
            var outbound = CopyAsync(_local.GetStream(), _proxy.GetStream(), _stop.Token);
            var inbound = CopyAsync(_proxy.GetStream(), _local.GetStream(), _stop.Token);
            await Task.WhenAny(outbound, inbound).ConfigureAwait(false);
            _stop.Cancel(); _local.Close(); _proxy.Close();
            try { await Task.WhenAll(outbound, inbound).ConfigureAwait(false); }
            catch (Exception) when (_stop.IsCancellationRequested) { }
        }
        catch (Exception error) when (!_stop.IsCancellationRequested)
        {
            Volatile.Write(ref _failure, error);
        }
        catch (Exception) when (_stop.IsCancellationRequested) { }
        finally { _listener.Stop(); _local?.Dispose(); _proxy?.Dispose(); }
    }

    /// <summary>流关闭时结束搬运，不缓存或记录经过的数据。</summary>
    private static async Task CopyAsync(Stream input, Stream output, CancellationToken token)
    {
        byte[] buffer = new byte[32 * 1024];
        while (true)
        {
            int read = await input.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) return;
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop(); _local?.Dispose(); _proxy?.Dispose();
        await _relay.ConfigureAwait(false);
        _stop.Dispose();
    }
}
