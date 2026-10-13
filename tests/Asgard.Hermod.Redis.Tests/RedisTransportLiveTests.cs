using System.Net;
using System.Net.Sockets;
using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.Tests;
using Xunit;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>真实 Redis 流量经过本机代理和 TLS 边界，避免只验证参数而漏掉连接适配错误。</summary>
public sealed class RedisTransportLiveTests
{
    public static bool IsEnabled => RedisLiveTests.IsEnabled;
    private static int Port => int.Parse(Environment.GetEnvironmentVariable("HERMOD_REDIS_TEST_PORT")!, System.Globalization.CultureInfo.InvariantCulture);

    [Theory(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    [InlineData("socks5")]
    [InlineData("http")]
    public async Task AuthenticatedProxy_CarriesRealRedisRequests(string protocol)
    {
        await using var proxy = new LiveProxyServer(protocol, Port);
        var parameters = Direct(Port);
        parameters["route"] = "proxy"; parameters["proxy.type"] = protocol;
        parameters["proxy.host"] = "127.0.0.1"; parameters["proxy.port"] = proxy.Port.ToString();
        parameters["proxy.user"] = "proxy-user"; parameters["proxy.password"] = " proxy-password ";
        await using var session = await OpenAsync(parameters);
        Assert.True(proxy.Authenticated); await VerifyReadAsync(session);
    }

    [Fact(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    public async Task SshStreamAdapter_UsesSharedForwardingContractForEveryConnection()
    {
        var forwarding = new TestForwarding(); var parameters = Direct(Port); parameters["route"] = "ssh";
        parameters["ssh.host"] = "fixture.invalid"; parameters["ssh.user"] = "fixture-user";
        await using var session = await OpenAsync(parameters, forwarding);
        Assert.True(forwarding.Calls > 0); await VerifyReadAsync(session);
    }

    [Theory(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    [InlineData("localhost", true, true)]
    [InlineData("wrong.invalid", true, false)]
    [InlineData("localhost", false, false)]
    public async Task Tls_RequiresTrustedCertificateAndMatchingHostname(string hostname, bool customTrust, bool accepted)
    {
        await using var bridge = new RedisTlsTestBridge(Port);
        var parameters = Direct(bridge.Port); parameters["tls"] = "yes"; parameters["tls.host"] = hostname;
        if (customTrust) { parameters["trust"] = "custom"; parameters["ca"] = bridge.AuthorityPath; }
        if (accepted)
        {
            try { await using var session = await OpenAsync(parameters); await VerifyReadAsync(session); }
            catch (RedisException) { Assert.Fail(string.Join(Environment.NewLine, bridge.Failures)); }
        }
        else { var error = await Assert.ThrowsAsync<RedisException>(() => OpenAsync(parameters)); Assert.DoesNotContain(bridge.AuthorityPath, error.Message); }
    }

    private static Dictionary<string, string> Direct(int port) => new() { ["host"] = "127.0.0.1", ["port"] = port.ToString(), ["timeout"] = "2" };
    [Fact(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    public async Task MutualTls_LoadsPemKeyUsingWindowsSupportedStorage()
    {
        await using var bridge = new RedisTlsTestBridge(Port, requireClient: true);
        var parameters = Direct(bridge.Port); parameters["tls"] = "yes"; parameters["tls.host"] = "localhost";
        parameters["trust"] = "custom"; parameters["ca"] = bridge.AuthorityPath; parameters["mtls"] = "yes";
        parameters["certificate"] = bridge.CertificatePath; parameters["privateKey"] = bridge.PrivateKeyPath;
        await using var session = await OpenAsync(parameters); await VerifyReadAsync(session);
        Assert.True(bridge.ClientAuthenticated);
    }
    private static Task<IRedisSession> OpenAsync(Dictionary<string, string> parameters, ISshTcpStreamFactory? ssh = null) => new RedisSessionFactory(ssh ?? new TestForwarding()).OpenAsync(
        new InstanceConnectionOptions { TypeId = RedisIds.Type, ProviderId = RedisIds.Standalone, Parameters = parameters }, TestContext.Current.CancellationToken);
    private static async Task VerifyReadAsync(IRedisSession session)
    {
        var snapshot = await session.ReadAsync(0, RedisKeyId.FromText("hermod-absent:" + Guid.NewGuid().ToString("N")), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("none", snapshot.Type);
    }

    // 仅替换 SSH 协议边界；真实主机信任与认证由 Ssh 模块回归覆盖。
    private sealed class TestForwarding : ISshTcpStreamFactory
    {
        internal int Calls { get; private set; }
        public async Task<Stream> OpenAsync(InstanceConnectionOptions options, string host, int port, CancellationToken cancellationToken)
        {
            Calls++; Assert.Equal(SshIds.Direct, options.ProviderId); Assert.Equal("fixture-user", options.Parameters["user"]);
            var client = new TcpClient();
            try { await client.ConnectAsync(host, port, cancellationToken); return client.GetStream(); }
            catch { client.Dispose(); throw; }
        }
    }
}
