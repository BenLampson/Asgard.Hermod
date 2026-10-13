using System.Text;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>验证代理握手字节、认证、响应限制与目标 banner 保留。</summary>
public sealed class SshProxyProtocolTests
{
    /// <summary>SOCKS5 分片回复不会吞掉随后到达的 SSH 字节。</summary>
    [Theory]
    [InlineData("target.example", 3)]
    [InlineData("127.0.0.1", 1)]
    [InlineData("::1", 4)]
    public async Task SocksAddressesAndBannerAreCorrect(string host, int kind)
    {
        using var stream = new ScriptedDuplexStream([5, 0, 5, 0, 0, 1, 0, 0, 0, 0, 0, 0, (byte)'S']);
        await SshProxyProtocol.ConnectAsync(stream, Options(host: host), TestContext.Current.CancellationToken);
        byte[] request = stream.Requests.ToArray();
        Assert.Equal(new byte[] { 5, 1, 0, 5, 1, 0, (byte)kind }, request[..7]);
        Assert.Equal(new byte[] { 0, 22 }, request[^2..]);
        Assert.Equal('S', stream.ReadByte());
    }

    /// <summary>用户名密码认证按 UTF-8 和长度前缀编码，不修剪密码。</summary>
    [Fact]
    public async Task SocksAuthenticationPreservesPassword()
    {
        using var stream = new ScriptedDuplexStream([5, 2, 1, 0, 5, 0, 0, 3, 1, (byte)'x', 0, 0]);
        await SshProxyProtocol.ConnectAsync(stream, Options(user: "u", password: " p "), TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 5, 2, 0, 2, 1, 1, (byte)'u', 3, 32, (byte)'p', 32 }, stream.Requests.ToArray()[..11]);
    }

    /// <summary>HTTP CONNECT 正确处理 IPv6、代理认证和紧接响应头的 SSH banner。</summary>
    [Fact]
    public async Task HttpConnectStopsExactlyAtHeaderBoundary()
    {
        using var stream = new ScriptedDuplexStream(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connected\r\nX-Test: value\r\n\r\nSSH-2.0-test"));
        await SshProxyProtocol.ConnectAsync(stream, Options("http", "::1", "u", " p "), TestContext.Current.CancellationToken);
        string request = Encoding.ASCII.GetString(stream.Requests.ToArray());
        Assert.StartsWith("CONNECT [::1]:22 HTTP/1.1\r\nHost: [::1]:22\r\n", request);
        Assert.Contains("Proxy-Authorization: Basic " + Convert.ToBase64String("u: p "u8), request);
        Assert.Equal('S', stream.ReadByte());
    }

    /// <summary>拒绝失败和畸形状态，错误不回显响应内容。</summary>
    [Theory]
    [InlineData("HTTP/1.1 407 password=secret\r\n\r\n")]
    [InlineData("SSH-2.0 200 bad\r\n\r\n")]
    [InlineData("HTTP/1.1 2000 bad\r\n\r\n")]
    public async Task HttpFailureIsSafe(string response)
    {
        using var stream = new ScriptedDuplexStream(Encoding.ASCII.GetBytes(response));
        var error = await Assert.ThrowsAsync<SshException>(() => SshProxyProtocol.ConnectAsync(stream, Options("http"), TestContext.Current.CancellationToken));
        Assert.Equal("SSH_PROXY_FAILED", error.Code);
        Assert.DoesNotContain("secret", error.Message);
    }

    /// <summary>响应头上限防止无限等待和分配。</summary>
    [Fact]
    public async Task HttpHeaderHasBoundedLength()
    {
        using var stream = new ScriptedDuplexStream(Encoding.ASCII.GetBytes(new string('x', 9000)));
        await Assert.ThrowsAsync<SshException>(() => SshProxyProtocol.ConnectAsync(stream, Options("http"), TestContext.Current.CancellationToken));
        Assert.Equal(8192, stream.Position);
    }

    /// <summary>SOCKS5 不能接受未提供的认证或拒绝状态。</summary>
    [Theory]
    [InlineData(5, 2)]
    [InlineData(5, 255)]
    [InlineData(4, 0)]
    public async Task SocksInvalidMethodsFail(byte version, byte method)
    {
        using var stream = new ScriptedDuplexStream([version, method]);
        await Assert.ThrowsAsync<SshException>(() => SshProxyProtocol.ConnectAsync(stream, Options(), TestContext.Current.CancellationToken));
    }

    /// <summary>取消必须传递到实际握手 I/O。</summary>
    [Fact]
    public async Task CancellationIsHonored()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var stream = new ScriptedDuplexStream([]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SshProxyProtocol.ConnectAsync(stream, Options(), cancellation.Token));
    }

    private static SshConnectionOptions Options(string protocol = "socks5", string host = "target.example", string user = "", string password = "") => new()
    {
        Route = SshIds.Proxy,
        Target = new() { Host = host, User = "tester", Authentication = SshIds.Password },
        ProxyType = protocol,
        ProxyUser = user,
        ProxyPassword = password,
    };
}
