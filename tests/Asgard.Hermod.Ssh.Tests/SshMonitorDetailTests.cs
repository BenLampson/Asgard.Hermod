using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>验证结构化只读详情、协议区域、缺失数据与远端输入边界。</summary>
public sealed class SshMonitorDetailTests
{
    /// <summary>进程 CPU 按核心数归一；协议数值不随测试机区域改变。</summary>
    [Fact]
    public void ProcessFieldsUseInvariantNumbersAndExcludeArguments()
    {
        var previous = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var rows = SshMonitorDetailReader.ParseProcesses("42 app 120.4 2.5 4096 Sl worker name\n7 root 9.0 1.0 1024 S nginx\n", 4);
            Assert.Equal(30.1, rows[0].CpuPercent, 5); Assert.Equal(2.5, rows[0].MemoryPercent);
            Assert.Equal(4194304, rows[0].ResidentBytes); Assert.Equal("worker name", rows[0].Name);
            Assert.DoesNotContain("args", SshMonitorDetailReader.ProcessCommand, StringComparison.Ordinal);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    /// <summary>容量按字节排序，挂载路径可含空格，tmpfs 不计入持久汇总。</summary>
    [Fact]
    public void MountsPreservePathsAndClassifyMemoryFileSystems()
    {
        var rows = SshMonitorDetailReader.ParseMounts("Filesystem Type 1024-blocks Used Available Capacity Mounted on\n/dev/sda1 ext4 1000 880 120 88% /data files\ntmpfs tmpfs 500 0 500 0% /run\n");
        Assert.Equal("/data files", rows[0].Path); Assert.Equal(1024000, rows[0].TotalBytes);
        Assert.Equal(88, rows[0].UsedPercent); Assert.True(rows[0].IsPersistent); Assert.False(rows[1].IsPersistent);
    }

    /// <summary>Docker 的协议状态与用户定义名称分离，日志只能使用完整容器 ID。</summary>
    [Fact]
    public void ContainersParseJsonWithoutUsingDisplayNamesAsCommands()
    {
        string id = new('a', 64);
        var row = Assert.Single(SshMonitorDetailReader.ParseContainers($$"""
            {"ID":"{{id}}","Names":"name; echo secret","Image":"repo/image:tag","State":"running","Status":"Up 2 hours (unhealthy)","Ports":"127.0.0.1:8080->80/tcp"}
            """));
        Assert.Equal("unhealthy", row.Health); Assert.Equal("running", row.State);
        Assert.DoesNotContain(row.Name, SshMonitorDetailReader.ContainerLogCommand(row.Id), StringComparison.Ordinal);
        Assert.EndsWith(id, SshMonitorDetailReader.ContainerLogCommand(row.Id));
    }

    /// <summary>日志不接受选项、名称、短 ID 或 shell 元字符。</summary>
    [Theory]
    [InlineData("--help")]
    [InlineData("nginx")]
    [InlineData("a123b456")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa;z")]
    public void LogsRejectNonCanonicalIdentifiers(string id) => Assert.ThrowsAny<Exception>(() => SshMonitorDetailReader.ContainerLogCommand(id));

    /// <summary>畸形或负数不能成为假的零值。</summary>
    [Theory]
    [InlineData("1 root NaN 1 1 S nginx")]
    [InlineData("1 root 1.0 1.0 -1 S nginx")]
    [InlineData("not a process row")]
    public void InvalidProcessRowsAreRejected(string output) => Assert.ThrowsAny<Exception>(() => SshMonitorDetailReader.ParseProcesses(output, 4));

    /// <summary>网卡计数器单独差分，新增接口不能抬高全机流量。</summary>
    [Fact]
    public void NetworkDeltasFollowEachInterfaceAndInvalidateChangedAggregate()
    {
        var monitor = new SshHostMonitor();
        var first = monitor.Parse(SshWorkspaceProtocolTests.Sample("100 0 100 800", 1000, 2000), 0);
        Assert.Null(Assert.Single(first.Interfaces).SentPerSecond);
        var second = monitor.Parse(SshWorkspaceProtocolTests.Sample("120 0 120 860", 4000, 5000)
            .Replace("__LOAD__", "eth1: 900000 0 0 0 0 0 0 0 800000 0\n__LOAD__", StringComparison.Ordinal), Stopwatch.Frequency);
        Assert.Null(second.SentPerSecond); Assert.Equal(3000, second.Interfaces[0].SentPerSecond);
        Assert.Null(second.Interfaces[1].SentPerSecond);
    }

    /// <summary>列表有上限，避免大量远端记录导致 UI 无限增长。</summary>
    [Fact]
    public void DetailListsAreBounded() => Assert.Equal(SshMonitorDetailReader.MaximumItems,
        SshMonitorDetailReader.ParseProcesses(string.Join('\n', Enumerable.Repeat("1 root 0 0 1 S init", 600)), 1).Count);
}
