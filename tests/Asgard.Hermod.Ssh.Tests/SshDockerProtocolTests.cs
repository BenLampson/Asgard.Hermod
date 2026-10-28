using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>验证真实 Docker JSON 形状、版本探测、能力选择与 shell 参数边界。</summary>
public sealed class SshDockerProtocolTests
{
    private static readonly string Id = new('a', 64);
    private static readonly SshDockerEnvironment Plugin = new(true, true, "27.5.1", "27.5.1", "2.32.4", "docker compose", true);

    /// <summary>CPU 可超过百分之一百；协议小数不依赖开发机语言，累计流量通过真实时间差分。</summary>
    [Fact]
    public void ResourceCountersUseInvariantUnitsAndElapsedTime()
    {
        var previous = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var reader = new SshDockerStatsReader();
            var first = Assert.Single(reader.Parse(Stats("10kB / 20kB"), 0, DateTimeOffset.UnixEpoch));
            Assert.Equal(250.5, first.CpuPercent); Assert.Equal(1610612736, first.MemoryUsed); Assert.Equal(4294967296, first.MemoryLimit);
            Assert.Null(first.ReceivedPerSecond); Assert.Equal(24, first.Processes);
            var second = Assert.Single(reader.Parse(Stats("14kB / 26kB"), 2 * Stopwatch.Frequency, DateTimeOffset.UnixEpoch.AddSeconds(2)));
            Assert.Equal(2000, second.ReceivedPerSecond); Assert.Equal(3000, second.SentPerSecond);
            Assert.Equal(0, second.ReadPerSecond);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    /// <summary>重置、缺失及过期基线都不能生成伪造的瞬时零值。</summary>
    [Fact]
    public void ResetAndGapsInvalidateRatesAndMissingFieldsStayUnknown()
    {
        var reader = new SshDockerStatsReader(); reader.Parse(Stats("10kB / 20kB"), 0, DateTimeOffset.UnixEpoch);
        var reset = Assert.Single(reader.Parse(Stats("1B / 1B"), Stopwatch.Frequency, DateTimeOffset.UnixEpoch.AddSeconds(1)));
        Assert.Null(reset.ReceivedPerSecond);
        var gap = Assert.Single(reader.Parse(Stats("2B / 2B"), 40 * Stopwatch.Frequency, DateTimeOffset.UnixEpoch.AddSeconds(40)));
        Assert.Null(gap.ReceivedPerSecond);
        reader.Reset(); Assert.Null(Assert.Single(reader.Parse(Stats("3B / 3B"), 41 * Stopwatch.Frequency, DateTimeOffset.UnixEpoch.AddSeconds(41))).ReceivedPerSecond);
        var missing = Assert.Single(reader.Parse($$"""{"ID":"{{Id}}","CPUPerc":"--","MemUsage":"--","MemPerc":"--","NetIO":"--","BlockIO":"--","PIDs":"--"}""", 0, DateTimeOffset.UnixEpoch));
        Assert.Null(missing.MemoryUsed); Assert.Null(missing.Processes); Assert.Null(missing.CpuPercent);
    }

    /// <summary>畸形数值与不完整标识不能污染采样。</summary>
    [Theory]
    [InlineData("NaN%")]
    [InlineData("-1%")]
    [InlineData("1,2%")]
    [InlineData("Infinity%")]
    public void InvalidPercentagesAreRejected(string percent) => Assert.ThrowsAny<Exception>(() => new SshDockerStatsReader().Parse(
        Stats("1B / 1B").Replace("250.5%", percent, StringComparison.Ordinal), 0, DateTimeOffset.UnixEpoch));

    /// <summary>资源读取只接受完整 ID，不从列表名称生成命令。</summary>
    [Theory]
    [InlineData("--help")]
    [InlineData("name; touch /tmp/pwn")]
    [InlineData("a123b456")]
    public void StatsRejectArbitraryTargets(string id) => Assert.ThrowsAny<Exception>(() => SshDockerStatsReader.Command([id]));

    /// <summary>探测区分守护进程访问、插件与旧版 Compose，永不把原始错误当成版本。</summary>
    [Theory]
    [InlineData("plugin", "docker compose")]
    [InlineData("legacy", "docker-compose")]
    public void EnvironmentSelectsDetectedComposeEntryPoint(string mode, string expected)
    {
        var environment = SshDockerEnvironment.Parse(new(Probe(mode), 0, false));
        Assert.True(environment.CanAccess); Assert.True(environment.SupportsStats); Assert.Equal(expected, environment.ComposeCommand);
        Assert.Equal("27.5.1", environment.EngineVersion);
        Assert.False(SshDockerEnvironment.Parse(new("secret error", 42, false)).Installed);
        Assert.ThrowsAny<Exception>(() => SshDockerEnvironment.Parse(new("secret error", 1, false)));
    }

    /// <summary>旧版 Compose 使用已检测入口；显式目录和文件都作为 shell 单个参数转义。</summary>
    [Fact]
    public void ComposeQuotesPathsAndNeverBuildsImages()
    {
        var legacy = Plugin with { ComposeCommand = "docker-compose", ComposeVersion = "1.29.2" };
        var plan = SshDockerCommandBuilder.Build("compose-up", new(ProjectDirectory: "/srv/a'b $(bad)", ComposeFile: "compose file.yaml", Service: "api"), legacy);
        Assert.StartsWith("cd '/srv/a'\"'\"'b $(bad)' && docker-compose -f 'compose file.yaml' up -d --no-build 'api'", plan.Command);
        Assert.True(plan.Operation.ChangesState);
        Assert.ThrowsAny<Exception>(() => SshDockerCommandBuilder.Build("compose-up", new(ProjectDirectory: "relative"), Plugin));
        Assert.ThrowsAny<Exception>(() => SshDockerCommandBuilder.Build("compose-up", new(ProjectDirectory: "/srv", Service: "--build"), Plugin));
    }

    /// <summary>修改参数不会变更已经生成的计划，跟踪日志只走独立终端。</summary>
    [Fact]
    public void PlansAreExactAndCapabilitiesGateUnsupportedOperations()
    {
        var options = new SshDockerCommandOptions(Id, Follow: true);
        var logs = SshDockerCommandBuilder.Build("logs", options, Plugin);
        Assert.Equal("docker logs --tail 500 --since 1h --timestamps --follow " + Id, logs.Command); Assert.True(logs.RunsInTerminal);
        Assert.False(logs.Operation.ChangesState);
        Assert.Equal("docker restart --time 10 " + Id, SshDockerCommandBuilder.Build("restart", new(Id), Plugin).Command);
        Assert.ThrowsAny<Exception>(() => SshDockerCommandBuilder.Build("stats", new(Id), Plugin with { SupportsStats = false }));
        Assert.ThrowsAny<Exception>(() => SshDockerCommandBuilder.Build("pull", new(Image: "x; rm -rf /"), Plugin));
        Assert.ThrowsAny<Exception>(() => SshDockerCommandBuilder.Build("logs", new(Id, Since: "$(bad)"), Plugin));
        Assert.ThrowsAny<Exception>(() => SshDockerCommandBuilder.Build("logs", new(Id), Plugin with { CliVersion = "1.12.0" }));
        Assert.ThrowsAny<Exception>(() => SshDockerCommandBuilder.Build("compose-ps", new(ProjectDirectory: "/srv"), Plugin with { ComposeVersion = "1.17.0" }));
    }

    /// <summary>结构化输出保留长仓库、无标签镜像及共享 ID 的多条标签。</summary>
    [Fact]
    public void ImageRowsRetainDockerFieldsAndDistinctTags()
    {
        string first = ImageJson("registry.example.com/very/long/repository", "latest", Id);
        string second = ImageJson("registry.example.com/very/long/repository", "stable", Id);
        string dangling = ImageJson("<none>", "<none>", new string('b', 64));
        var images = SshDockerImageReader.Parse(new(string.Join('\n', first, second, dangling), 0, false), out bool limited);
        Assert.False(limited); Assert.Equal(3, images.Count);
        Assert.Equal("registry.example.com/very/long/repository", images[0].Repository);
        Assert.Equal("latest", images[0].Tag); Assert.Equal("stable", images[1].Tag);
        Assert.Equal("sha256:" + Id, images[0].Id); Assert.Equal(Id[..12], images[0].ShortId);
        Assert.Equal("<none>", images[2].Repository); Assert.Equal("7 days ago", images[0].Created); Assert.Equal("264MB", images[0].Size);
    }

    /// <summary>超限只保留完整 JSON 行并提示不完整；失败和畸形数据不能成为镜像行。</summary>
    [Fact]
    public void ImageRowsRejectErrorsAndMarkIncompleteOutput()
    {
        string valid = ImageJson("nginx", "latest", Id);
        var images = SshDockerImageReader.Parse(new(valid + "\n{\"Repository\":", -1, true), out bool limited);
        Assert.True(limited); Assert.Single(images);
        Assert.ThrowsAny<Exception>(() => SshDockerImageReader.Parse(new("password=private", 1, false), out _));
        Assert.ThrowsAny<Exception>(() => SshDockerImageReader.Parse(new("{\"Repository\":\"nginx\"}", 0, false), out _));
        Assert.ThrowsAny<Exception>(() => SshDockerImageReader.Parse(new("{\"Repository\":", -1, true), out _));
        images = SshDockerImageReader.Parse(new(string.Join('\n', Enumerable.Repeat(valid, 501)), 0, false), out limited);
        Assert.True(limited); Assert.Equal(500, images.Count);
    }

    private static string ImageJson(string repository, string tag, string id) =>
        System.Text.Json.JsonSerializer.Serialize(new { Repository = repository, Tag = tag, ID = "sha256:" + id, CreatedSince = "7 days ago", Size = "264MB" });

    private static string Stats(string network) => $$"""{"ID":"{{Id}}","CPUPerc":"250.5%","MemUsage":"1.5GiB / 4GiB","MemPerc":"37.5%","NetIO":"{{network}}","BlockIO":"1MB / 2MB","PIDs":"24"}""";
    private static string Probe(string mode) => "__HERMOD_VERSION__\n{\"Client\":{\"Version\":\"27.5.1\"},\"Server\":{\"Version\":\"27.5.1\"}}\n__HERMOD_ACCESS__0\n__HERMOD_COMPOSE__" + mode + " 2.32.4\n__HERMOD_STATS__\n--no-stream --no-trunc --format\n";
}
