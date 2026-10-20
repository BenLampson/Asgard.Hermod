using System.Text.Json;
using System.Text.RegularExpressions;

namespace Asgard.Hermod.Ssh;

/// <summary>只保留版本与能力，不保存探测原文、连接参数或 SDK 错误。</summary>
public sealed record SshDockerEnvironment(bool Installed, bool CanAccess, string CliVersion, string EngineVersion,
    string ComposeVersion, string ComposeCommand, bool SupportsStats)
{
    /// <summary>基础模板明确支持 Docker CLI 20.10 及以上；更旧或无法解析的版本不猜测参数兼容性。</summary>
    public bool SupportsBaseCommands => AtLeast(CliVersion, 20, 10);
    /// <summary>Compose 模板支持 1.29 及以上，命令入口仍由实际探测确定。</summary>
    public bool SupportsComposeCommands => AtLeast(ComposeVersion, 1, 29);
    /// <summary>固定探测只读能力；Compose 插件优先，旧版入口仅在插件不可用时尝试。</summary>
    public const string ProbeCommand = "command -v docker >/dev/null 2>&1 || exit 42; " +
        "printf '__HERMOD_VERSION__\\n'; docker version --format '{{json .}}' 2>/dev/null; code=$?; printf '\\n__HERMOD_ACCESS__%s\\n' \"$code\"; " +
        "if value=$(docker compose version --short 2>/dev/null); then printf '__HERMOD_COMPOSE__plugin %s\\n' \"$value\"; " +
        "elif value=$(docker-compose version --short 2>/dev/null); then printf '__HERMOD_COMPOSE__legacy %s\\n' \"$value\"; fi; " +
        "printf '__HERMOD_STATS__\\n'; docker stats --help 2>/dev/null";

    /// <summary>检测结果必须具有受认可的结构；守护进程不可用与未安装分开呈现。</summary>
    public static SshDockerEnvironment Parse(SshCommandResult result)
    {
        if (result.ExitCode == 42) return new(false, false, "", "", "", "", false);
        if (result.Truncated) throw Text.Exception("DockerEnvironmentFailed");
        var lines = result.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string? version = lines.FirstOrDefault(l => l.StartsWith('{'));
        if (!lines.Contains("__HERMOD_VERSION__") || version is null) throw Text.Exception("DockerEnvironmentFailed");
        using var json = JsonDocument.Parse(version);
        string cli = VersionOf(json.RootElement, "Client"), engine = VersionOf(json.RootElement, "Server");
        string compose = "", command = "";
        string? composeLine = lines.FirstOrDefault(l => l.StartsWith("__HERMOD_COMPOSE__", StringComparison.Ordinal));
        if (composeLine is not null)
        {
            string[] parts = composeLine[18..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && ValidVersion(parts[1]))
            {
                command = parts[0] switch { "plugin" => "docker compose", "legacy" => "docker-compose", _ => "" };
                if (command.Length > 0) compose = parts[1];
            }
        }
        string help = result.Output.Split("__HERMOD_STATS__", StringSplitOptions.None).Last();
        bool stats = help.Contains("--no-stream", StringComparison.Ordinal) && help.Contains("--no-trunc", StringComparison.Ordinal) && help.Contains("--format", StringComparison.Ordinal);
        return new(true, engine.Length > 0 && lines.Contains("__HERMOD_ACCESS__0"), cli, engine, compose, command, stats);
    }

    private static string VersionOf(JsonElement root, string key) => root.TryGetProperty(key, out var component) && component.ValueKind == JsonValueKind.Object &&
        component.TryGetProperty("Version", out var version) && version.ValueKind == JsonValueKind.String && ValidVersion(version.GetString() ?? "") ? version.GetString()! : "";
    private static bool ValidVersion(string value) => Regex.IsMatch(value, @"\Av?[0-9][a-zA-Z0-9._+\-]{0,63}\z", RegexOptions.CultureInvariant);
    private static bool AtLeast(string value, int major, int minor)
    {
        string[] parts = value.TrimStart('v').Split('.');
        return parts.Length >= 2 && int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int first) &&
            int.TryParse(new string(parts[1].TakeWhile(char.IsAsciiDigit).ToArray()), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int second) &&
            (first > major || first == major && second >= minor);
    }
}
