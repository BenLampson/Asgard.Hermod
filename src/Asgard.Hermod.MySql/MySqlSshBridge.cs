using System.Net;
using System.Net.Sockets;

namespace Asgard.Hermod.MySql;

/// <summary>仅监听回环地址，将 SDK 的主连接和取消连接转发到已有 SSH 流。</summary>
internal sealed class MySqlSshBridge : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<CancellationToken, Task<Stream>> _open;
    private readonly List<Task> _relays = [];
    private readonly Task _accept;
    private Stream? _first;
    internal MySqlSshBridge(Stream first, Func<CancellationToken, Task<Stream>> open)
    {
        _first = first; _open = open; _listener.Start(4);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port; _accept = AcceptAsync();
    }
    internal int Port { get; }
    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                lock (_relays) { _relays.RemoveAll(task => task.IsCompleted); _relays.Add(RelayAsync(client)); }
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested) { }
    }
    private async Task RelayAsync(TcpClient client)
    {
        using (client)
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            Stream? remote = null;
            try
            {
                client.NoDelay = true; remote = Interlocked.Exchange(ref _first, null) ?? await _open(linked.Token).ConfigureAwait(false);
                var input = remote.CopyToAsync(client.GetStream(), linked.Token);
                var output = client.GetStream().CopyToAsync(remote, linked.Token);
                await Task.WhenAny(input, output).ConfigureAwait(false);
                linked.Cancel(); client.Close(); remote.Close();
                try { await Task.WhenAll(input, output).ConfigureAwait(false); } catch (Exception) when (linked.IsCancellationRequested) { }
            }
            catch (Exception) { /* 不记录转发字节和第三方异常；连接层给出安全失败。 */ }
            finally { if (remote is not null) await remote.DisposeAsync().ConfigureAwait(false); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop(); Interlocked.Exchange(ref _first, null)?.Dispose();
        await _accept.ConfigureAwait(false);
        Task[] pending; lock (_relays) pending = _relays.ToArray();
        await Task.WhenAll(pending).ConfigureAwait(false); _stop.Dispose();
    }
}
