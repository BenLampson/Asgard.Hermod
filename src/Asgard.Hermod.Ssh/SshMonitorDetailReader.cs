using System.Globalization;
using System.Text.Json;

namespace Asgard.Hermod.Ssh;

/// <summary>有界只读详情解析器：只接受固定命令与完整输出，数字始终按协议区域解析。</summary>
public static class SshMonitorDetailReader
{
    /// <summary>最多展示五百项，超出限制由调用者显示已达到读取上限。</summary>
    public const int MaximumItems = 500;

    /// <summary>进程命令不读取参数，避免把凭据或用户的完整命令行带入监测。</summary>
    public const string ProcessCommand = "LC_ALL=C ps -eo pid=,user=,pcpu=,pmem=,rss=,stat=,comm= --sort=-pcpu";
    /// <summary>包括已停止容器；daemon 失败保留为一般不可用，不猜测原始错误文本。</summary>
    public const string ContainerCommand = "command -v docker >/dev/null 2>&1 || exit 42; " +
        "if [ -S /var/run/docker.sock ] && { [ ! -r /var/run/docker.sock ] || [ ! -w /var/run/docker.sock ]; }; then exit 43; fi; " +
        "docker ps -a --no-trunc --format '{{json .}}'";
    /// <summary>容量使用 KiB 原值，文件系统类型单独输出，不解析人类可读的容量后缀。</summary>
    public const string MountCommand = "LC_ALL=C df -PTk";

    /// <summary>容器 ID 必须是完整十六进制 ID；不能传名称、选项、路径或 shell 字符。</summary>
    public static string ContainerLogCommand(string id)
    {
        if (id is not { Length: 64 } || id.Any(c => !char.IsAsciiHexDigit(c))) throw Text.Exception("SshMonitorUnavailable");
        return "docker logs --tail 100 --timestamps " + id;
    }

    /// <summary>读取 ps 的七个固定字段，最后的名称可包含空格。</summary>
    public static IReadOnlyList<SshProcessSnapshot> ParseProcesses(string output, int cores)
    {
        var items = new List<SshProcessSnapshot>();
        foreach (string line in Lines(output))
        {
            var fields = line.Split((char[]?)null, 7, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 7 || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0)
                throw Text.Exception("SshMonitorUnavailable");
            items.Add(new(pid, fields[6], fields[1], fields[5], Math.Clamp(Decimal(fields[2]) / Math.Max(1, cores), 0, 100),
                Math.Clamp(Decimal(fields[3]), 0, 100), Bytes(fields[4])));
        }
        return items;
    }

    /// <summary>Docker 一行一个 JSON；状态与健康检查转为稳定协议值，不显示原始 Status。</summary>
    public static IReadOnlyList<SshContainerSnapshot> ParseContainers(string output)
    {
        var items = new List<SshContainerSnapshot>();
        foreach (string line in Lines(output))
        {
            using var json = JsonDocument.Parse(line);
            var value = json.RootElement;
            string id = Value(value, "ID");
            _ = ContainerLogCommand(id);
            string status = Value(value, "Status");
            string health = status.Contains("(unhealthy)", StringComparison.Ordinal) ? "unhealthy" : status.Contains("(healthy)", StringComparison.Ordinal) ? "healthy"
                : status.Contains("(health: starting)", StringComparison.Ordinal) ? "starting" : "none";
            items.Add(new(id, Value(value, "Names"), Value(value, "Image"), Value(value, "State"), health, Value(value, "Ports")));
        }
        return items;
    }

    /// <summary>POSIX df 保证一行一条记录；挂载路径中的空格和转义不会拆成额外字段。</summary>
    public static IReadOnlyList<SshMountSnapshot> ParseMounts(string output)
    {
        var items = new List<SshMountSnapshot>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("Filesystem", StringComparison.Ordinal)) throw Text.Exception("SshMonitorUnavailable");
        foreach (string line in Lines(string.Join('\n', lines.Skip(1))))
        {
            var fields = line.Split((char[]?)null, 7, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 7) throw Text.Exception("SshMonitorUnavailable");
            items.Add(new(fields[0], fields[6], fields[1], Bytes(fields[2]), Bytes(fields[3]), Bytes(fields[4])));
        }
        return items;
    }

    /// <summary>读取 /proc 接口计数；忽略回环，不允许负数或不完整的计数行。</summary>
    internal static IReadOnlyList<SshNetworkSnapshot> ParseNetworks(IEnumerable<string> rows, string addresses)
    {
        var result = new List<SshNetworkSnapshot>();
        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(addresses) ? "[]" : addresses);
        foreach (string row in rows)
        {
            int colon = row.IndexOf(':');
            if (colon < 0) continue;
            string name = row[..colon].Trim();
            if (name == "lo") continue;
            var values = row[(colon + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length < 9) throw Text.Exception("SshMonitorUnavailable");
            var info = json.RootElement.EnumerateArray().FirstOrDefault(n => Value(n, "ifname") == name);
            string ip = "", state = "UNKNOWN"; int? mtu = null;
            if (info.ValueKind == JsonValueKind.Object)
            {
                state = Value(info, "operstate");
                if (info.TryGetProperty("mtu", out var m) && m.TryGetInt32(out int number)) mtu = number;
                if (info.TryGetProperty("addr_info", out var a)) ip = string.Join(", ", a.EnumerateArray().Where(n => Value(n, "family") == "inet").Select(n => Value(n, "local")));
            }
            result.Add(new(name, state, ip, mtu, Integer(values[8]), Integer(values[0]), null, null));
        }
        return result;
    }

    private static IEnumerable<string> Lines(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaximumItems);
    private static string Value(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long Bytes(string value) => checked(Integer(value) * 1024);
    private static long Integer(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number >= 0 ? number : throw Text.Exception("SshMonitorUnavailable");
    private static double Decimal(string value) => double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number) && number >= 0 ? number : throw Text.Exception("SshMonitorUnavailable");
}
