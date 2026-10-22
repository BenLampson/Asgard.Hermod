using System.Diagnostics;
using System.Text.Json;
using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.UI.Tests;

/// <summary>仅用于可重复的离线验收：协议替身提供真实格式，生产界面不包含任何演示数据。</summary>
internal sealed class SshMonitorTestData
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private int _sample;
    internal bool FailSampling { get; set; }
    internal SshWorkspaceTestConnection CreateConnection() => new() { Handler = HandleAsync };

    internal Task<SshCommandResult> HandleAsync(string command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (command.Contains("__CPU__", StringComparison.Ordinal))
        {
            if (FailSampling) throw new IOException("private password=never-display");
            _sample++;
            double elapsed = Stopwatch.GetElapsedTime(_started).TotalSeconds;
            return Task.FromResult(new SshCommandResult(Sample(_sample, 100000 + (long)(elapsed * 91.2 * 1024), 200000 + (long)(elapsed * 95.1 * 1024)), 0, false));
        }
        if (command == SshMonitorDetailReader.ProcessCommand) return Task.FromResult(new SshCommandResult(Processes, 0, false));
        if (command == SshMonitorDetailReader.MountCommand) return Task.FromResult(new SshCommandResult(Mounts, 0, false));
        if (command == SshMonitorDetailReader.ContainerCommand) return Task.FromResult(new SshCommandResult(Containers, 0, false));
        if (command == SshDockerEnvironment.ProbeCommand) return Task.FromResult(new SshCommandResult(DockerEnvironment, 0, false));
        if (command.StartsWith("LC_ALL=C docker stats ", StringComparison.Ordinal)) return Task.FromResult(new SshCommandResult(DockerStats, 0, false));
        if (command.StartsWith("docker logs ", StringComparison.Ordinal)) return Task.FromResult(new SshCommandResult("2026-10-15T06:32:00Z [notice] nginx started\n203.0.113.8 GET /health 200\n", 0, false));
        return Task.FromResult(new SshCommandResult("", 1, false));
    }

    internal static string Sample(int n, long rx = 1000, long tx = 2000) => $$"""
        __SYSTEM__
        Linux 6.8.0-100-generic
        __CPU__
        cpu {{1000 + n * 27}} 0 {{1000 + n * 27}} {{8000 + n * 946}} 0 0 0 0
        __MEM__
        MemTotal: 7549747 kB
        MemAvailable: 4928307 kB
        __DISK__
        Filesystem 1024-blocks Used Available Capacity Mounted on
        /dev/sda1 82208358 33047760 49160598 41% /
        __NET__
        lo: 9999999 0 0 0 0 0 0 0 9999999 0
        eth0: {{rx}} 0 0 0 0 0 0 0 {{tx}} 0
        docker0: 128000 0 0 0 0 0 0 0 256000 0
        __LOAD__
        0.18 0.12 0.09
        __UPTIME__
        1560000.0
        __CORES__
        4
        __IP__
        [{"ifname":"eth0","operstate":"UP","mtu":1500,"addr_info":[{"family":"inet","local":"203.0.113.24"}]},{"ifname":"docker0","operstate":"UP","mtu":1500,"addr_info":[{"family":"inet","local":"172.17.0.1"}]}]
        """;

    internal const string Processes = """
        1842 app 7.2 14.4 1090519 R python3
        912 www-data 4.8 1.2 90624 S nginx
        2201 app 4.4 8.5 641740 R dotnet
        803 redis 2.0 2.5 188723 S redis-server
        1056 root 1.6 1.6 120832 S containerd
        1 root 0.4 0.3 22630 S systemd
        """;
    internal const string Mounts = """
        Filesystem Type 1024-blocks Used Available Capacity Mounted on
        /dev/sda1 ext4 82208358 33047760 49160598 41% /
        /dev/sdb1 ext4 206359756 181596585 24763171 88% /data
        /dev/sda2 ext4 974848 540672 434176 56% /boot
        tmpfs tmpfs 754974 1843 753131 1% /run
        tmpfs tmpfs 3774873 0 3774873 0% /dev/shm
        """;
    internal static string Containers => string.Join('\n', new[]
    {
        ("web-nginx", "nginx:1.27-alpine", "running", "Up 18 days (healthy)", "0.0.0.0:443->443/tcp"),
        ("hermod-api", "hermod-api:2026.10", "running", "Up 18 days (healthy)", "127.0.0.1:8080->8080/tcp"),
        ("jobs-worker", "hermod-worker:2026.10", "running", "Up 18 days (unhealthy)", ""),
        ("redis-cache", "redis:7-alpine", "running", "Up 18 days", "127.0.0.1:6379->6379/tcp"),
        ("backup-once", "postgres:17-alpine", "exited", "Exited (0) 2 hours ago", ""),
    }.Select((c, i) => JsonSerializer.Serialize(new { ID = new string((char)('a' + i), 64), Names = c.Item1, Image = c.Item2, State = c.Item3, Status = c.Item4, Ports = c.Item5 })));

    internal const string DockerEnvironment = "__HERMOD_VERSION__\n{\"Client\":{\"Version\":\"27.5.1\"},\"Server\":{\"Version\":\"27.5.1\"}}\n__HERMOD_ACCESS__0\n__HERMOD_COMPOSE__plugin 2.32.4\n__HERMOD_STATS__\n--no-stream --no-trunc --format\n";
    internal static string DockerStats => string.Join('\n', Enumerable.Range(0, 4).Select(i => JsonSerializer.Serialize(new
    {
        ID = new string((char)('a' + i), 64),
        CPUPerc = i == 1 ? "28.4%" : "4.2%",
        MemUsage = "428MiB / 1GiB",
        MemPerc = "41.8%",
        NetIO = "124kB / 36kB",
        BlockIO = "2.4MB / 180kB",
        PIDs = "24",
    })));
}
