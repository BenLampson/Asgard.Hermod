using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.Ssh;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>覆盖参数边界、认证切换和诊断脱敏，不依赖机器显示语言。</summary>
public sealed class SshOptionsTests
{
    /// <summary>协议默认值在未指定参数时仍可正确解析。</summary>
    [Fact]
    public void DefaultsAndPasswordWhitespaceArePreserved()
    {
        var options = SshConnectionOptions.Parse(Connection());
        Assert.Equal(22, options.Target.Port);
        Assert.Equal(15, options.TimeoutSeconds);
        Assert.Equal("  secret  ", options.Target.Password);
        Assert.DoesNotContain("secret", options.ToString());
        Assert.DoesNotContain("secret", options.Target.ToString());
    }

    /// <summary>未使用认证字段不得阻止当前认证方式。</summary>
    [Theory]
    [InlineData(SshIds.Agent)]
    [InlineData(SshIds.PrivateKey)]
    public void InactivePasswordIsIgnored(string authentication)
    {
        var parameters = new Dictionary<string, string>(Connection().Parameters)
        { ["auth"] = authentication, ["password"] = "invalid\npassword", ["key"] = "C:/keys/test" };
        var options = SshConnectionOptions.Parse(Connection(parameters: parameters));
        Assert.Equal(authentication, options.Target.Authentication);
        Assert.Empty(options.Target.Password);
    }

    /// <summary>用户输入不能逃逸主机、端口和稳定选项的边界。</summary>
    [Theory]
    [InlineData("port", "0")]
    [InlineData("port", "65536")]
    [InlineData("port", "22.5")]
    [InlineData("timeout", "301")]
    [InlineData("timeout", "0")]
    [InlineData("host", "host/path")]
    [InlineData("host", "user@host")]
    [InlineData("host", "host\r\ninjected")]
    [InlineData("auth", "Password")]
    public void InvalidParametersFailSafely(string key, string value)
    {
        var parameters = new Dictionary<string, string>(Connection().Parameters) { [key] = value };
        Exception error = Assert.ThrowsAny<Exception>(() => SshConnectionOptions.Parse(Connection(parameters: parameters)));
        Assert.DoesNotContain("secret", error.Message);
    }

    /// <summary>跳板和目标的认证值独立，IPv6 不能被当作带端口的主机拒绝。</summary>
    [Fact]
    public void JumpIdentityIsIndependent()
    {
        var parameters = new Dictionary<string, string>(Connection().Parameters)
        { ["host"] = "::1", ["jump.host"] = "gateway.example", ["jump.user"] = "jump-user", ["jump.auth"] = SshIds.Agent };
        var options = SshConnectionOptions.Parse(Connection(SshIds.Jump, parameters));
        Assert.Equal("::1", options.Target.Host);
        Assert.Equal(SshIds.Password, options.Target.Authentication);
        Assert.Equal(SshIds.Agent, options.JumpHost!.Authentication);
        Assert.Equal("jump-user", options.JumpHost.User);
    }

    /// <summary>非本模块配置必须拒绝。</summary>
    [Fact]
    public void UnknownRouteIsRejected() => Assert.ThrowsAny<Exception>(() => SshConnectionOptions.Parse(Connection("unknown")));

    /// <summary>即使原异常包含凭据和服务端响应，也只返回稳定安全错误。</summary>
    [Fact]
    public void ErrorsDoNotExposeSdkMessages()
    {
        var error = SshConnectionFactory.SafeError(new InvalidOperationException("password=secret server banner"));
        Assert.Equal("SSH_CONNECTION_FAILED", error.Code);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("secret", error.Message);
    }

    /// <summary>测试配置使用无外部意义的凭据。</summary>
    internal static InstanceConnectionOptions Connection(string route = SshIds.Direct, IReadOnlyDictionary<string, string>? parameters = null) => new()
    {
        TypeId = SshIds.Type,
        ProviderId = route,
        Parameters = parameters ?? new Dictionary<string, string> { ["host"] = "target.example", ["user"] = "tester", ["password"] = "  secret  " },
    };
}
