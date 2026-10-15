using System.Diagnostics;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>确定性验证监测差分、重启计数器和远端 POSIX 路径，不连接开发者服务器。</summary>
public sealed class SshWorkspaceProtocolTests
{
    /// <summary>首次采样无差分，第二次使用真实 CPU 与网络计数器计算。</summary>
    [Fact]
    public void MonitorComputesDeltasWithoutInventingFirstSample()
    {
        var monitor = new SshHostMonitor();
        var first = monitor.Parse(Sample("100 0 100 800 0 0 0 0 999 999", 1000, 2000), 1000);
        Assert.Null(first.CpuPercent); Assert.Null(first.SentPerSecond);
        var second = monitor.Parse(Sample("120 0 120 860 0 0 0 0 999 999", 4000, 5000), 1000 + Stopwatch.Frequency);
        Assert.Equal(40d, second.CpuPercent); Assert.Equal(3000d, second.SentPerSecond); Assert.Equal(3000d, second.ReceivedPerSecond);
        Assert.Equal(8192L * 1024, second.MemoryTotal); Assert.Equal(6144L * 1024, second.MemoryUsed);
        Assert.Equal(25000L * 1024, second.DiskUsed); Assert.Equal(100000L * 1024, second.DiskTotal);
    }

    /// <summary>远端计数器重置不展示负速率。</summary>
    [Fact]
    public void RebootedCountersInvalidateRates()
    {
        var monitor = new SshHostMonitor();
        monitor.Parse(Sample("100 0 100 800", 1000, 2000), 0);
        var reset = monitor.Parse(Sample("1 0 1 8", 1, 2), Stopwatch.Frequency);
        Assert.Null(reset.CpuPercent); Assert.Null(reset.ReceivedPerSecond); Assert.Null(reset.SentPerSecond);
    }

    /// <summary>非 Linux 响应不能被识别为零负载。</summary>
    [Fact]
    public void UnsupportedMonitorDataIsRejected() => Assert.ThrowsAny<Exception>(() => new SshHostMonitor().Parse("not a Linux sample", 0));

    /// <summary>诊断只从固定 ID 选择，不把 UI 或用户字符串拼入 shell。</summary>
    [Fact]
    public void UnknownMonitorSectionCannotBecomeShellCommand() => Assert.ThrowsAny<Exception>(() => SshHostMonitor.DetailCommand("network; touch /tmp/injected"));

    /// <summary>合法文件名可含空格和引号，协议层不将其交给 shell。</summary>
    [Theory]
    [InlineData("/etc/nginx", "site 'one'.conf", "/etc/nginx/site 'one'.conf")]
    [InlineData("/", "中文.txt", "/中文.txt")]
    public void RemotePathsUsePosixSeparators(string directory, string name, string expected) => Assert.Equal(expected, SshRemotePath.CombineName(directory, name));

    /// <summary>输入名称不能越出当前目录。</summary>
    [Theory]
    [InlineData("../secret")]
    [InlineData("..")]
    [InlineData("a\\b")]
    [InlineData("bad\nname")]
    public void FileNamesRejectTraversalAndControls(string name) => Assert.ThrowsAny<Exception>(() => SshRemotePath.CombineName("/safe", name));

    internal static string Sample(string cpu, long rx, long tx) => $"__SYSTEM__\nLinux 6.8\n__CPU__\ncpu {cpu}\n__MEM__\nMemTotal: 8192 kB\nMemAvailable: 2048 kB\n__DISK__\nFilesystem 1024-blocks Used Available Capacity Mounted on\n/dev/vda 100000 25000 75000 25% /\n__NET__\nlo: 999999 0 0 0 0 0 0 0 999999 0\neth0: {rx} 0 0 0 0 0 0 0 {tx} 0\n__LOAD__\n0.1 0.2 0.3\n__UPTIME__\n60.0\n";
}
