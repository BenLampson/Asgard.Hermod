using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Asgard.Hermod.Ssh;

/// <summary>只协商 TCP 代理；SSH 加密、认证和身份校验仍由协议库负责。</summary>
internal static class SshProxyProtocol
{
    /// <summary>在 SSH 握手开始前建立目标隧道，不消费目标 SSH 的首字节。</summary>
    internal static Task ConnectAsync(Stream stream, SshConnectionOptions options, CancellationToken token) =>
        options.ProxyType == "http" ? HttpAsync(stream, options, token) : SocksAsync(stream, options, token);

    /// <summary>SOCKS5 支持无认证、用户名密码、IPv4、IPv6 和远程 DNS。</summary>
    private static async Task SocksAsync(Stream stream, SshConnectionOptions options, CancellationToken token)
    {
        bool credentials = options.ProxyUser.Length != 0;
        await stream.WriteAsync(credentials ? new byte[] { 5, 2, 0, 2 } : new byte[] { 5, 1, 0 }, token).ConfigureAwait(false);
        byte[] response = new byte[2];
        await stream.ReadExactlyAsync(response, token).ConfigureAwait(false);
        if (response[0] != 5 || response[1] is not (0 or 2) || response[1] == 2 && !credentials) throw Failed();
        if (response[1] == 2)
        {
            byte[] user = Encoding.UTF8.GetBytes(options.ProxyUser), password = Encoding.UTF8.GetBytes(options.ProxyPassword);
            if (user.Length > 255 || password.Length > 255) throw new SshException("SshProxyCredentialsInvalid", "SSH_PROXY_CREDENTIALS_INVALID");
            var request = new byte[3 + user.Length + password.Length];
            request[0] = 1; request[1] = (byte)user.Length; user.CopyTo(request, 2);
            request[2 + user.Length] = (byte)password.Length; password.CopyTo(request, 3 + user.Length);
            try { await stream.WriteAsync(request, token).ConfigureAwait(false); }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(request);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(password);
            }
            await stream.ReadExactlyAsync(response, token).ConfigureAwait(false);
            if (response[0] != 1 || response[1] != 0) throw Failed();
        }
        IPAddress? address = IPAddress.TryParse(options.Target.Host, out var parsed) ? parsed : null;
        if (address is null && !options.RemoteDns)
            address = (await Dns.GetHostAddressesAsync(options.Target.Host, token).ConfigureAwait(false)).FirstOrDefault()
                ?? throw Failed();
        byte[] host = address?.GetAddressBytes() ?? Encoding.ASCII.GetBytes(new System.Globalization.IdnMapping().GetAscii(options.Target.Host));
        if (address is null && host.Length > 255) throw Failed();
        using var packet = new MemoryStream();
        packet.Write([5, 1, 0, address is null ? (byte)3 : address.AddressFamily == AddressFamily.InterNetwork ? (byte)1 : (byte)4]);
        if (address is null) packet.WriteByte((byte)host.Length);
        packet.Write(host); packet.WriteByte((byte)(options.Target.Port >> 8)); packet.WriteByte((byte)options.Target.Port);
        await stream.WriteAsync(packet.ToArray(), token).ConfigureAwait(false);
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        if (header[0] != 5 || header[1] != 0 || header[2] != 0) throw Failed();
        int length = header[3] switch { 1 => 4, 4 => 16, 3 => -1, _ => throw Failed() };
        if (length == -1) { await stream.ReadExactlyAsync(response.AsMemory(0, 1), token).ConfigureAwait(false); length = response[0]; }
        await stream.ReadExactlyAsync(new byte[length + 2], token).ConfigureAwait(false);
    }

    /// <summary>HTTP CONNECT 只接受成功状态，响应头有明确上限且不会吞掉 SSH banner。</summary>
    private static async Task HttpAsync(Stream stream, SshConnectionOptions options, CancellationToken token)
    {
        string host = IPAddress.TryParse(options.Target.Host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? "[" + address + "]" : new System.Globalization.IdnMapping().GetAscii(options.Target.Host);
        string destination = host + ":" + options.Target.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string authorization = options.ProxyUser.Length == 0 ? "" : "Proxy-Authorization: Basic "
            + Convert.ToBase64String(Encoding.UTF8.GetBytes(options.ProxyUser + ":" + options.ProxyPassword)) + "\r\n";
        byte[] request = Encoding.ASCII.GetBytes("CONNECT " + destination + " HTTP/1.1\r\nHost: " + destination + "\r\n" + authorization + "\r\n");
        try { await stream.WriteAsync(request, token).ConfigureAwait(false); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(request); }
        using var response = new MemoryStream();
        byte[] current = new byte[1]; uint tail = 0;
        while (response.Length < 8192)
        {
            await stream.ReadExactlyAsync(current, token).ConfigureAwait(false);
            response.WriteByte(current[0]); tail = (tail << 8) | current[0];
            if (tail == 0x0D0A0D0A)
            {
                string status = Encoding.ASCII.GetString(response.ToArray()).Split("\r\n", 2)[0];
                string[] parts = status.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[0] is not ("HTTP/1.1" or "HTTP/1.0") || parts[1] != "200") throw Failed();
                return;
            }
        }
        throw Failed();
    }

    /// <summary>代理原始响应不作为错误消息返回。</summary>
    private static SshException Failed() => new("SshProxyFailed", "SSH_PROXY_FAILED");
}
