using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Asgard.Hermod.Ssh;

/// <summary>有界读取 Docker stats；只向命令传递经过验证的完整容器 ID。</summary>
public sealed class SshDockerStatsReader
{
    private Dictionary<string, SshDockerResourceSnapshot> _previous = [];
    private long? _timestamp;

    /// <summary>固定非流式采样，避免在后台遗留持续运行的远端进程。</summary>
    public static string Command(IEnumerable<string> ids)
    {
        var targets = ids.Take(SshMonitorDetailReader.MaximumItems + 1).ToArray();
        if (targets.Length is 0 or > SshMonitorDetailReader.MaximumItems) throw Text.Exception("DockerInvalidParameters");
        foreach (string id in targets) ValidateId(id);
        return "LC_ALL=C docker stats --no-stream --no-trunc --format '{{json .}}' " + string.Join(' ', targets);
    }

    /// <summary>完整 ID 不接受名称、短 ID、选项或 shell 字符。</summary>
    public static void ValidateId(string id)
    {
        if (id is not { Length: 64 } || id.Any(c => !char.IsAsciiHexDigit(c))) throw Text.Exception("DockerInvalidParameters");
    }

    /// <summary>协议数值使用固定区域；缺失或不支持的字段保持空，畸形字段拒绝整次采样。</summary>
    public IReadOnlyList<SshDockerResourceSnapshot> Parse(string output, long timestamp, DateTimeOffset capturedAt)
    {
        var result = new List<SshDockerResourceSnapshot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        double seconds = _timestamp is { } before ? (timestamp - before) / (double)Stopwatch.Frequency : 0;
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (result.Count >= SshMonitorDetailReader.MaximumItems) throw Text.Exception("DockerStatsUnavailable");
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement; string id = Value(root, "ID"); ValidateId(id);
            if (!seen.Add(id)) throw Text.Exception("DockerStatsUnavailable");
            var memory = Pair(Value(root, "MemUsage")); var network = Pair(Value(root, "NetIO")); var block = Pair(Value(root, "BlockIO"));
            string pids = Value(root, "PIDs");
            int? count = Missing(pids) ? null : int.TryParse(pids, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 0 ? n : throw Text.Exception("DockerStatsUnavailable");
            var sample = new SshDockerResourceSnapshot(id, capturedAt, Percent(Value(root, "CPUPerc")), memory.First, memory.Second,
                Percent(Value(root, "MemPerc")), network.First, network.Second, block.First, block.Second, count);
            if (seconds is > 0 and <= 30 && _previous.TryGetValue(id, out var old)) sample = sample with
            {
                ReceivedPerSecond = Rate(sample.ReceivedBytes, old.ReceivedBytes, seconds),
                SentPerSecond = Rate(sample.SentBytes, old.SentBytes, seconds),
                ReadPerSecond = Rate(sample.ReadBytes, old.ReadBytes, seconds),
                WrittenPerSecond = Rate(sample.WrittenBytes, old.WrittenBytes, seconds),
            };
            result.Add(sample);
        }
        _previous = result.ToDictionary(s => s.Id, StringComparer.Ordinal); _timestamp = timestamp;
        return result;
    }

    /// <summary>失败、暂停或重连后丢弃差分基线，不跨缺口生成速率。</summary>
    public void Reset() { _previous.Clear(); _timestamp = null; }

    private static double? Rate(long? current, long? old, double seconds) => current is { } a && old is { } b && a >= b ? (a - b) / seconds : null;
    private static bool Missing(string value) => value is "" or "--" or "N/A" or "[no value]";
    private static string Value(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static double? Percent(string value)
    {
        if (Missing(value)) return null;
        if (!value.EndsWith('%') || !double.TryParse(value[..^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n) || n < 0)
            throw Text.Exception("DockerStatsUnavailable");
        return n;
    }
    private static (long? First, long? Second) Pair(string value)
    {
        if (Missing(value)) return (null, null);
        string[] parts = value.Split('/');
        if (parts.Length != 2) throw Text.Exception("DockerStatsUnavailable");
        return (Bytes(parts[0].Trim()), Bytes(parts[1].Trim()));
    }
    private static long? Bytes(string value)
    {
        if (Missing(value)) return null;
        int index = 0;
        while (index < value.Length && (char.IsAsciiDigit(value[index]) || value[index] == '.')) index++;
        if (!decimal.TryParse(value[..index], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number)) throw Text.Exception("DockerStatsUnavailable");
        decimal multiplier = value[index..].Trim() switch
        {
            "B" => 1,
            "kB" or "KB" => 1000,
            "MB" => 1000000,
            "GB" => 1000000000,
            "TB" => 1000000000000,
            "KiB" => 1024,
            "MiB" => 1048576,
            "GiB" => 1073741824,
            "TiB" => 1099511627776,
            _ => throw Text.Exception("DockerStatsUnavailable"),
        };
        if (number < 0 || number > long.MaxValue / multiplier) throw Text.Exception("DockerStatsUnavailable");
        return (long)(number * multiplier);
    }
}
