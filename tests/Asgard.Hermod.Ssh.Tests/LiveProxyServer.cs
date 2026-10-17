using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>仅监听本机的真实 TCP 代理边界，可承载首次核验后的第二次 SSH 握手。</summary>
internal sealed class LiveProxyServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accept;
    private readonly List<Task> _clients = [];
    private readonly string _protocol;
    private readonly int _sshPort;
    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    internal bool Authenticated { get; private set; }
    internal LiveProxyServer(string protocol, int sshPort)
    { _protocol = protocol; _sshPort = sshPort; _listener.Start(); _accept = AcceptAsync(); }
    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _clients.Add(RelayAsync(client));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private async Task RelayAsync(TcpClient client)
    {
        using (client)
        using (var upstream = new TcpClient())
        {
            try
            {
                var input = client.GetStream();
                if (_protocol == "http")
                {
                    var header = new StringBuilder(); byte[] one = new byte[1];
                    while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    { await input.ReadExactlyAsync(one, _stop.Token); header.Append((char)one[0]); }
                    Xunit.Assert.StartsWith($"CONNECT 127.0.0.1:{_sshPort} HTTP/1.1\r\n", header.ToString());
                    Xunit.Assert.Contains("Proxy-Authorization: Basic " + Convert.ToBase64String("proxy-user: proxy-password "u8), header.ToString());
                    Authenticated = true;
                    await input.WriteAsync("HTTP/1.1 200 Connected\r\n\r\n"u8.ToArray(), _stop.Token);
                }
                else
                {
                    byte[] greeting = new byte[2]; await input.ReadExactlyAsync(greeting, _stop.Token);
                    byte[] methods = new byte[greeting[1]]; await input.ReadExactlyAsync(methods, _stop.Token);
                    await input.WriteAsync(new byte[] { 5, 2 }, _stop.Token);
                    await input.ReadExactlyAsync(greeting, _stop.Token);
                    byte[] user = new byte[greeting[1]]; await input.ReadExactlyAsync(user, _stop.Token);
                    byte[] length = new byte[1]; await input.ReadExactlyAsync(length, _stop.Token);
                    byte[] password = new byte[length[0]]; await input.ReadExactlyAsync(password, _stop.Token);
                    Xunit.Assert.Equal("proxy-user", Encoding.UTF8.GetString(user)); Xunit.Assert.Equal(" proxy-password ", Encoding.UTF8.GetString(password));
                    Authenticated = true; await input.WriteAsync(new byte[] { 1, 0 }, _stop.Token);
                    byte[] request = new byte[10]; await input.ReadExactlyAsync(request, _stop.Token);
                    Xunit.Assert.Equal(new byte[] { 5, 1, 0, 1, 127, 0, 0, 1 }, request[..8]);
                    Xunit.Assert.Equal(_sshPort, (request[8] << 8) | request[9]);
                    await input.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 0 }, _stop.Token);
                }
                await upstream.ConnectAsync(IPAddress.Loopback, _sshPort, _stop.Token);
                using var relay = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                Task send = input.CopyToAsync(upstream.GetStream(), relay.Token);
                Task receive = upstream.GetStream().CopyToAsync(input, relay.Token);
                await Task.WhenAny(send, receive); relay.Cancel();
                try { await Task.WhenAll(send, receive); } catch (OperationCanceledException) { }
            }
            catch (IOException) { } // 主机核验拒绝后 SDK 主动结束这一连接。
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }
    }
    public async ValueTask DisposeAsync()
    { _stop.Cancel(); await _accept; _listener.Stop(); await Task.WhenAll(_clients); _stop.Dispose(); }
}
