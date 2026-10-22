using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>仅连接显式配置的回环隔离 Docker fixture，不读取用户实例、凭据或 SSH 配置。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshDockerLocalIntegrationTests(WpfTestFixture fixture)
{
    /// <summary>专用变量只能指定端口，主机始终硬编码为 IPv4 回环地址。</summary>
    public static bool IsConfigured => int.TryParse(Environment.GetEnvironmentVariable("HERMOD_TEST_DOCKER_SSH_PORT"),
        NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65535;

    /// <summary>真实 SSH、环境检测、按钮动作和刷新后状态保持一致，其他容器不受影响。</summary>
    [Fact(Skip = "Configure the loopback-only Docker-in-Docker fixture.", SkipUnless = nameof(IsConfigured))]
    public Task ContainerActionsAndMonitoringUseAnIsolatedRealDockerEngine() => fixture.RunOffscreenAsync(async () =>
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var trust = new LocalTrust(); var factory = new SshConnectionFactory(trust, trust);
        string port = Environment.GetEnvironmentVariable("HERMOD_TEST_DOCKER_SSH_PORT")!;
        var options = new InstanceConnectionOptions
        {
            TypeId = SshIds.Type,
            ProviderId = SshIds.Direct,
            Parameters = new Dictionary<string, string>
            { ["host"] = "127.0.0.1", ["port"] = port, ["user"] = "tester", ["password"] = "hermod-test-only", ["timeout"] = "5" },
        };
        await using var opened = await factory.OpenAsync(options, 100, 30, timeout.Token);
        var connection = Assert.IsAssignableFrom<ISshWorkspaceConnection>(opened);
        var approvals = new Approvals();
        using var model = new SshDockerCommandsViewModel(() => connection, () => "tester@127.0.0.1:" + port,
            approvals, () => true, (_, _) => Task.CompletedTask);
        await model.InitializeAsync(); Assert.Empty(model.Error);
        model.TimeoutSeconds = "1";
        Assert.True(model.Environment!.SupportsStats); Assert.True(model.Environment.SupportsComposeCommands);
        Assert.Null(model.SelectedContainer);
        model.SelectedContainer = model.Containers.Single(c => c.Name == "hermod-ux-web");
        string id = model.SelectedContainer.Id;
        await model.ActionCommand.ExecuteAsync("logs");
        Assert.Empty(model.Error); Assert.Contains("hermod-local-log-ready", model.Output, StringComparison.Ordinal);
        Assert.Equal(0, approvals.Count);
        await model.ActionCommand.ExecuteAsync("stop");
        Assert.Empty(model.Error); Assert.Equal(id, model.SelectedContainer!.Id); Assert.Equal("exited", model.SelectedContainer.State);
        Assert.True(model.ActionCommand.CanExecute("start")); Assert.False(model.ActionCommand.CanExecute("stop"));
        await model.ActionCommand.ExecuteAsync("start");
        Assert.Empty(model.Error); Assert.True(model.SelectedContainer!.IsRunning);
        await model.ActionCommand.ExecuteAsync("restart");
        Assert.Empty(model.Error); Assert.True(model.SelectedContainer!.IsRunning); Assert.Equal(id, model.SelectedContainer.Id);
        Assert.Equal(3, approvals.Count); Assert.Contains("hermod-ux-web", model.Output, StringComparison.Ordinal);
        Assert.True(model.Containers.Single(c => c.Name == "hermod-ux-unhealthy").IsUnhealthy);
        Assert.Equal("created", model.Containers.Single(c => c.Name == "hermod-ux-stopped").State);
        await using var monitor = new SshMonitorViewModel();
        await monitor.StartAsync(connection); monitor.IsPaused = true; monitor.Section = "docker";
        await monitor.RefreshDetailsCommand.ExecuteAsync();
        Assert.Empty(monitor.SectionError);
        monitor.SelectedContainer = monitor.Containers.Single(c => c.Id == id);
        Assert.NotNull(monitor.SelectedContainer.Resources); Assert.NotNull(monitor.SelectedContainer.CpuPercent);
        Assert.NotNull(monitor.SelectedContainer.MemoryBytes); Assert.NotEmpty(monitor.ContainerCpuPoints);
        Assert.Equal("127.0.0.1", Assert.Single(trust.Keys).Host);
    });

    /// <summary>Compose 操作使用同一隔离引擎与显式测试目录，不在服务器构建镜像。</summary>
    [Fact(Skip = "Configure the loopback-only Docker-in-Docker fixture.", SkipUnless = nameof(IsConfigured))]
    public Task ComposeUsesTheSelectedProjectAndReadsItsLogs() => fixture.RunOffscreenAsync(async () =>
    {
        var trust = new LocalTrust();
        string port = Environment.GetEnvironmentVariable("HERMOD_TEST_DOCKER_SSH_PORT")!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var opened = await new SshConnectionFactory(trust, trust).OpenAsync(new()
        {
            TypeId = SshIds.Type,
            ProviderId = SshIds.Direct,
            Parameters = new Dictionary<string, string>
            { ["host"] = "127.0.0.1", ["port"] = port, ["user"] = "tester", ["password"] = "hermod-test-only", ["timeout"] = "5" },
        }, 100, 30, timeout.Token);
        var connection = Assert.IsAssignableFrom<ISshWorkspaceConnection>(opened);
        using var model = new SshDockerCommandsViewModel(() => connection, () => "tester@127.0.0.1:" + port,
            new Approvals(), () => true, (_, _) => Task.CompletedTask);
        await model.InitializeAsync(); Assert.Empty(model.Error);
        model.Select("compose-up", null); model.ProjectDirectory = "/tmp/hermod-compose";
        Assert.Contains("--no-build", model.Preview, StringComparison.Ordinal);
        await model.ExecuteCommand.ExecuteAsync(); Assert.Empty(model.Error);
        model.Select("compose-ps", null); await model.ExecuteCommand.ExecuteAsync();
        Assert.Empty(model.Error); Assert.Contains("worker", model.Output, StringComparison.Ordinal);
        model.Select("compose-logs", null); await model.ExecuteCommand.ExecuteAsync();
        Assert.Empty(model.Error); Assert.Contains("hermod-compose-ready", model.Output, StringComparison.Ordinal);
    });

    private sealed class Approvals : ISshWorkspaceDialogs
    {
        internal int Count { get; private set; }
        public Task<bool> ConfirmCloseAsync(string name) => Task.FromResult(false);
        public Task<bool> ConfirmActionAsync(string title, string detail, string preview) { Count++; return Task.FromResult(true); }
    }

    private sealed class LocalTrust : IHostKeyTrustStore, ISshHostKeyPrompt
    {
        internal List<TrustedHostKey> Keys { get; } = [];
        public Task<TrustedHostKey?> LoadAsync(string host, int port, CancellationToken cancellationToken = default)
            => Task.FromResult(Keys.SingleOrDefault(k => k.Host == host && k.Port == port));
        public Task<bool> TrustAsync(TrustedHostKey key, string? expectedFingerprint, CancellationToken cancellationToken = default)
        {
            Assert.Equal("127.0.0.1", key.Host); Keys.Add(key); return Task.FromResult(true);
        }
        public Task<bool> ConfirmAsync(SshHostKeyChallenge challenge, CancellationToken cancellationToken = default)
        { Assert.Equal("127.0.0.1", challenge.Presented.Host); return Task.FromResult(true); }
    }
}
