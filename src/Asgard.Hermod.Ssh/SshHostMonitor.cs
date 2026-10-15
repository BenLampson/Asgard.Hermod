using System.Diagnostics;
using System.Globalization;

namespace Asgard.Hermod.Ssh;

/// <summary>只执行固定、只读且有界的 Linux 采样命令；不需要远端安装守护进程。</summary>
public sealed class SshHostMonitor
{
    private const string SampleCommand = "LC_ALL=C; export LC_ALL; [ -r /proc/stat ] && [ -r /proc/meminfo ] || exit 42; " +
        "printf '__SYSTEM__\\n'; uname -sr; printf '__CPU__\\n'; head -n 1 /proc/stat; " +
        "printf '__MEM__\\n'; cat /proc/meminfo; printf '__DISK__\\n'; df -Pk /; " +
        "printf '__NET__\\n'; cat /proc/net/dev; printf '__LOAD__\\n'; cat /proc/loadavg; " +
        "printf '__UPTIME__\\n'; cut -d ' ' -f 1 /proc/uptime";
    private (long Total, long Idle, long Rx, long Tx, long Timestamp)? _previous;

    /// <summary>采样不重叠；调用者在重连后创建新的采样器。</summary>
    public async Task<SshHostSnapshot> ReadAsync(ISshWorkspaceConnection connection, CancellationToken cancellationToken)
    {
        var result = await connection.ExecuteCommandAsync(SampleCommand, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.Truncated) throw Text.Exception("SshMonitorUnavailable");
        return Parse(result.Output, Stopwatch.GetTimestamp());
    }

    /// <summary>只从固定 ID 选取诊断命令；页面标题不参与命令选择。</summary>
    public static string DetailCommand(string section) => section switch
    {
        "processes" => "LC_ALL=C ps -eo pid,user,pcpu,pmem,comm --sort=-pcpu | head -n 31",
        "docker" => "command -v docker >/dev/null 2>&1 || exit 42; docker ps --format 'table {{.Names}}\\t{{.Image}}\\t{{.Status}}' | head -n 51",
        "network" => "LC_ALL=C; ip -brief address 2>/dev/null; cat /proc/net/dev",
        "disks" => "LC_ALL=C df -Ph | head -n 51",
        _ => throw Text.Exception("SshMonitorUnavailable"),
    };

    /// <summary>独立解析入口支持确定性测试；协议数字始终使用 invariant。</summary>
    public SshHostSnapshot Parse(string output, long timestamp)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string section = "";
        foreach (string line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("__", StringComparison.Ordinal) && line.EndsWith("__", StringComparison.Ordinal))
            { section = line; sections[section] = []; }
            else if (line.Length > 0 && sections.TryGetValue(section, out var rows)) rows.Add(line);
        }
        if (!sections.TryGetValue("__CPU__", out var cpuRows) || !sections.TryGetValue("__MEM__", out var memory))
            throw Text.Exception("SshMonitorUnavailable");
        var cpu = Words(cpuRows.FirstOrDefault() ?? "").Skip(1).Take(8).Select(Number).ToArray();
        if (cpu.Length < 4) throw Text.Exception("SshMonitorUnavailable");
        long total = cpu.Sum(), idle = cpu[3] + (cpu.Length > 4 ? cpu[4] : 0);
        var mem = memory.Select(Words).Where(row => row.Length >= 2).ToDictionary(row => row[0], row => Number(row[1]), StringComparer.Ordinal);
        long memoryTotal = mem.GetValueOrDefault("MemTotal:") * 1024;
        long memoryAvailable = mem.GetValueOrDefault("MemAvailable:", mem.GetValueOrDefault("MemFree:")) * 1024;
        long rx = 0, tx = 0;
        foreach (string row in sections.GetValueOrDefault("__NET__") ?? [])
        {
            int colon = row.IndexOf(':');
            if (colon < 0 || row[..colon].Trim() == "lo") continue;
            var values = Words(row[(colon + 1)..]);
            if (values.Length >= 9) { rx += Number(values[0]); tx += Number(values[8]); }
        }
        double? cpuPercent = null, sent = null, received = null;
        if (_previous is { } old)
        {
            long delta = total - old.Total;
            if (delta > 0 && idle >= old.Idle) cpuPercent = Math.Clamp(100.0 * (delta - (idle - old.Idle)) / delta, 0, 100);
            double seconds = (timestamp - old.Timestamp) / (double)Stopwatch.Frequency;
            if (seconds > 0 && rx >= old.Rx && tx >= old.Tx) { received = (rx - old.Rx) / seconds; sent = (tx - old.Tx) / seconds; }
        }
        _previous = (total, idle, rx, tx, timestamp);
        var disk = Words(sections.GetValueOrDefault("__DISK__")?.LastOrDefault() ?? "");
        return new(sections.GetValueOrDefault("__SYSTEM__")?.FirstOrDefault() ?? "", cpuPercent,
            Math.Max(0, memoryTotal - memoryAvailable), memoryTotal,
            disk.Length >= 6 ? Number(disk[2]) * 1024 : 0, disk.Length >= 6 ? Number(disk[1]) * 1024 : 0,
            sent, received, sections.GetValueOrDefault("__LOAD__")?.FirstOrDefault() ?? "",
            sections.GetValueOrDefault("__UPTIME__")?.FirstOrDefault() ?? "");
    }

    private static string[] Words(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private static long Number(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) && number >= 0 ? number : 0;
}
