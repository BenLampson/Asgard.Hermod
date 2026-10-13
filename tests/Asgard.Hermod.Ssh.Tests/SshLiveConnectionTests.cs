using System.Globalization;
using System.Text;
using Asgard.Hermod.Abstractions.Models;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>显式配置隔离 OpenSSH 服务时运行端到端测试，不接触用户服务器或凭据。</summary>
public sealed class SshLiveConnectionTests
{
    /// <summary>只有本机隔离测试服务端口已配置才启用网络测试。</summary>
    public static bool IsConfigured => int.TryParse(Environment.GetEnvironmentVariable("HERMOD_TEST_SSH_PORT"), out int port) && port is > 0 and <= 65535;

    /// <summary>真实密码和加密 OpenSSH 私钥都通过身份核验建立 PTY。</summary>
    [Theory(Skip = "Configure the isolated OpenSSH fixture to enable network tests.", SkipUnless = nameof(IsConfigured))]
    [InlineData(SshIds.Password)]
    [InlineData(SshIds.PrivateKey)]
    public async Task DirectAuthenticationAndPtyWork(string authentication)
    {
        var trust = new LiveHostTrust(); var factory = new SshConnectionFactory(trust, trust);
        var options = Options(); var values = new Dictionary<string, string>(options.Parameters) { ["auth"] = authentication };
        if (authentication == SshIds.PrivateKey)
        { values["key"] = Required("HERMOD_TEST_SSH_KEY"); values["passphrase"] = "test-key-passphrase"; }
        options = new() { TypeId = options.TypeId, ProviderId = options.ProviderId, Parameters = values };
        using var timeout = Timeout();
        await using (var connection = await factory.OpenAsync(options, 90, 30, timeout.Token))
        {
            Assert.Single(connection.HostKeys); Assert.Single(trust.Challenges);
            await RoundTripAsync(connection, timeout.Token);
        }
        await using var cached = await factory.OpenAsync(options, 80, 24, timeout.Token);
        Assert.Single(trust.Challenges);
    }

    /// <summary>跳板和目标分别验证，即使地址相同也按独立端口记录。</summary>
    [Fact(Skip = "Configure the isolated OpenSSH fixture to enable network tests.", SkipUnless = nameof(IsConfigured))]
    public async Task JumpAuthenticatesBothHostsAndOpensTargetShell()
    {
        var options = Options(SshIds.Jump); var trust = new LiveHostTrust(); using var timeout = Timeout();
        await using var connection = await new SshConnectionFactory(trust, trust).OpenAsync(options, 90, 30, timeout.Token);
        Assert.Equal(2, trust.Challenges.Count); Assert.True(trust.Challenges[0].IsJumpHost); Assert.False(trust.Challenges[1].IsJumpHost);
        Assert.Equal(2, connection.HostKeys.Count); await RoundTripAsync(connection, timeout.Token);
    }

    /// <summary>两种代理承载实际 SSH 流量，信任绑定原目标而非本地转接端口。</summary>
    [Theory(Skip = "Configure the isolated OpenSSH fixture to enable network tests.", SkipUnless = nameof(IsConfigured))]
    [InlineData("socks5")]
    [InlineData("http")]
    public async Task AuthenticatedProxyCarriesRealSsh(string protocol)
    {
        int port = int.Parse(Required("HERMOD_TEST_SSH_PORT"), CultureInfo.InvariantCulture);
        await using var proxy = new LiveProxyServer(protocol, port);
        var values = new Dictionary<string, string>(Options().Parameters)
        {
            ["proxy.type"] = protocol,
            ["proxy.host"] = "127.0.0.1",
            ["proxy.port"] = proxy.Port.ToString(CultureInfo.InvariantCulture),
            ["proxy.user"] = "proxy-user",
            ["proxy.password"] = " proxy-password ",
        };
        var options = new InstanceConnectionOptions { TypeId = SshIds.Type, ProviderId = SshIds.Proxy, Parameters = values };
        var trust = new LiveHostTrust(); using var timeout = Timeout();
        await using var connection = await new SshConnectionFactory(trust, trust).OpenAsync(options, 90, 30, timeout.Token);
        Assert.True(proxy.Authenticated); Assert.Equal(port, Assert.Single(connection.HostKeys).Port);
        Assert.Equal("127.0.0.1", Assert.Single(trust.Challenges).Presented.Host);
        await RoundTripAsync(connection, timeout.Token);
    }

    /// <summary>已有指纹不匹配时拒绝覆盖，显式核验后才保存新身份。</summary>
    [Fact(Skip = "Configure the isolated OpenSSH fixture to enable network tests.", SkipUnless = nameof(IsConfigured))]
    public async Task ChangedFingerprintRequiresExplicitAcceptance()
    {
        var options = Options(); var trust = new LiveHostTrust { Accept = false }; using var timeout = Timeout();
        int port = int.Parse(options.Parameters["port"], CultureInfo.InvariantCulture);
        var old = new TrustedHostKey("127.0.0.1", port, "ssh-ed25519", "SHA256:" + new string('A', 43));
        await trust.TrustAsync(old, null, timeout.Token);
        var factory = new SshConnectionFactory(trust, trust);
        var rejected = await Assert.ThrowsAsync<SshException>(() => factory.OpenAsync(options, 90, 30, timeout.Token));
        Assert.Equal("SSH_HOST_NOT_TRUSTED", rejected.Code); Assert.True(Assert.Single(trust.Challenges).HasChanged);
        Assert.Equal(old, await trust.LoadAsync("127.0.0.1", port, timeout.Token));
        trust.Accept = true; await using var connection = await factory.OpenAsync(options, 90, 30, timeout.Token);
        Assert.NotEqual(old.Fingerprint, Assert.Single(connection.HostKeys).Fingerprint);
    }

    /// <summary>核验等待可取消，不能把迟到同意写入设备信任。</summary>
    [Fact(Skip = "Configure the isolated OpenSSH fixture to enable network tests.", SkipUnless = nameof(IsConfigured))]
    public async Task CancelWhileVerifyingDoesNotSaveTrust()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trust = new LiveHostTrust { Prompt = async token => { entered.SetResult(); await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token); return true; } };
        using var timeout = Timeout(); var factory = new SshConnectionFactory(trust, trust);
        var options = Options(); Task<ISshConnection> opening = factory.OpenAsync(options, 90, 30, timeout.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); timeout.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        Assert.Null(await trust.LoadAsync("127.0.0.1", int.Parse(options.Parameters["port"], CultureInfo.InvariantCulture), TestContext.Current.CancellationToken));
    }

    internal static InstanceConnectionOptions Options(string route = SshIds.Direct)
    {
        var values = new Dictionary<string, string>
        {
            ["host"] = "127.0.0.1",
            ["port"] = route == SshIds.Jump ? "22" : Required("HERMOD_TEST_SSH_PORT"),
            ["user"] = "tester",
            ["password"] = "hermod-test-only",
            ["timeout"] = "5",
        };
        if (route == SshIds.Jump)
        { values["jump.host"] = "127.0.0.1"; values["jump.port"] = Required("HERMOD_TEST_SSH_JUMP_PORT"); values["jump.user"] = "tester"; values["jump.password"] = "hermod-test-only"; }
        return new() { TypeId = SshIds.Type, ProviderId = route, Parameters = values };
    }
    internal static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Missing isolated SSH fixture configuration.");
    internal static CancellationTokenSource Timeout()
    { var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); source.CancelAfter(TimeSpan.FromSeconds(15)); return source; }
    internal static async Task RoundTripAsync(ISshConnection connection, CancellationToken token)
    {
        connection.Resize(101, 31);
        await connection.WriteAsync(Encoding.UTF8.GetBytes("printf '__HERMOD_UTF8__中文\\n'; stty size; exit 7\r"), token);
        var output = new MemoryStream(); byte[] bytes = new byte[4096];
        while (true) { int count = await connection.ReadAsync(bytes, token); if (count == 0) break; output.Write(bytes, 0, count); }
        string text = Encoding.UTF8.GetString(output.ToArray()); Assert.Contains("__HERMOD_UTF8__中文", text); Assert.Contains("31 101", text);
        Assert.Equal(7, await connection.GetExitCodeAsync(token));
    }
}
