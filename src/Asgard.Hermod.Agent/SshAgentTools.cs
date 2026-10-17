using System.Text.Json;
using System.Text.RegularExpressions;

namespace Asgard.Hermod.Agent;

/// <summary>仅固定诊断模板可以自动执行；任何任意命令都必须经过用户确认。</summary>
public static partial class SshAgentTools
{
    /// <summary>向模型声明有限的只读操作，不开放任意文件读取或 Shell 参数。</summary>
    public static IReadOnlyList<AgentToolDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new AgentToolDefinition("ssh_diagnostic", "Inspect the current Linux SSH host using a fixed read-only diagnostic. target is required only for service_status, service_logs and container_logs; otherwise use an empty string.", JsonSerializer.SerializeToElement(new
        {
            type = "object", properties = new
            {
                operation = new { type = "string", @enum = new[] { "host_summary", "disk_usage", "memory", "processes", "listening_ports", "service_status", "service_logs", "containers", "container_logs" } },
                target = new { type = "string", maxLength = 128 },
            },
            required = new[] { "operation", "target" }, additionalProperties = false,
        })),
        new AgentToolDefinition("ssh_command", "Propose any other shell command, including all modifications and restarts. The user must confirm the exact command on the current host. Explain its purpose and impact in your reply before requesting it. Execution starts in / and does not inherit terminal state. Never retry a denied or unknown-outcome action.", JsonSerializer.SerializeToElement(new
        {
            type = "object", properties = new { command = new { type = "string", maxLength = 8192 } }, required = new[] { "command" }, additionalProperties = false,
        })),
    });

    /// <summary>拒绝未知字段、重复字段、控制字符、选项注入及 Shell 插值，不能退化成自由诊断命令。</summary>
    public static SshAgentAction Parse(AgentToolCall call)
    {
        try
        {
            if (call.Arguments.Length > 10000) throw Text.Exception("SshAgentInvalidTool");
            using var document = JsonDocument.Parse(call.Arguments);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Text.Exception("SshAgentInvalidTool");
            var properties = root.EnumerateObject().ToArray();
            if (properties.Any(property => property.Value.ValueKind != JsonValueKind.String) || properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                throw Text.Exception("SshAgentInvalidTool");
            if (call.Name == "ssh_command" && properties.Length == 1 && root.TryGetProperty("command", out var command))
            {
                string value = command.GetString()!;
                if (string.IsNullOrWhiteSpace(value) || value.Length > 8192 || value.Contains('\0')) throw Text.Exception("SshAgentInvalidTool");
                return new("command", value, true);
            }
            if (call.Name != "ssh_diagnostic" || properties.Length != 2 || !root.TryGetProperty("operation", out var operation) || !root.TryGetProperty("target", out var targetValue))
                throw Text.Exception("SshAgentInvalidTool");
            string target = targetValue.GetString()!, id = operation.GetString()!;
            bool targeted = id is "service_status" or "service_logs" or "container_logs";
            if (targeted ? !SafeTarget().IsMatch(target) : target.Length != 0) throw Text.Exception("SshAgentInvalidTool");
            string script = id switch
            {
                "host_summary" => "uname -a; uptime",
                "disk_usage" => "df -h",
                "memory" => "free -m",
                "processes" => "ps -eo pid,ppid,pcpu,pmem,comm --sort=-pcpu",
                "listening_ports" => "ss -lntup",
                "service_status" => $"systemctl --no-pager --full status -- '{target}'",
                "service_logs" => $"journalctl --no-pager -n 100 -u '{target}'",
                "containers" => "docker ps -a --no-trunc",
                "container_logs" => $"docker logs --tail 100 --timestamps '{target}'",
                _ => throw Text.Exception("SshAgentInvalidTool"),
            };
            return new(id, script, false);
        }
        catch (JsonException) { throw Text.Exception("SshAgentInvalidTool"); }
    }

    // 不允许前导 '-'、通配符、路径、空白及任何 Shell 元字符。
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_.@:-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTarget();
}
