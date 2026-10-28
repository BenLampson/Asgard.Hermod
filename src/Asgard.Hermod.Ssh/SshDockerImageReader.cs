using System.Text.Json;

namespace Asgard.Hermod.Ssh;

/// <summary>使用固定只读命令读取 Docker 镜像，不解析按空格对齐的终端表格。</summary>
public static class SshDockerImageReader
{
    /// <summary>完整 ID 及每行 JSON 避免长名称、无标签镜像和空格破坏列边界。</summary>
    public const string Command = "docker images --no-trunc --format '{{json .}}'";

    /// <summary>仅展示完整记录；超出传输或条目限制时明确标记快照不完整。</summary>
    public static IReadOnlyList<SshDockerImageSnapshot> Parse(SshCommandResult result, out bool limited)
    {
        if (result.ExitCode != 0 && !(result.Truncated && result.ExitCode == -1)) throw Text.Exception("DockerImageReadFailed");
        string output = result.Output;
        limited = result.Truncated;
        if (result.Truncated && !output.EndsWith('\n'))
        {
            int end = output.LastIndexOf('\n');
            if (end < 0) throw Text.Exception("DockerImageReadFailed");
            output = output[..(end + 1)];
        }
        var items = new List<SshDockerImageSnapshot>();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (items.Count == SshMonitorDetailReader.MaximumItems) { limited = true; break; }
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            items.Add(new(Value(root, "Repository"), Value(root, "Tag"), Value(root, "ID"), Value(root, "CreatedSince"), Value(root, "Size")));
        }
        return items;
    }

    private static string Value(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text : throw Text.Exception("DockerImageReadFailed");
}
