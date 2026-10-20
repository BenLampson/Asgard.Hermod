using System.Globalization;
using System.Text.RegularExpressions;

namespace Asgard.Hermod.Ssh;

/// <summary>受控命令模板与 shell 参数转义；模型和远端输出不能添加可执行模板。</summary>
public static class SshDockerCommandBuilder
{
    /// <summary>命令目录只使用兼容的基础操作，不执行构建或隐式安装。</summary>
    public static IReadOnlyList<SshDockerCommandDefinition> Operations { get; } = Array.AsReadOnly<SshDockerCommandDefinition>(
    [
        new("containers", "DockerContainers", "DockerGroupContainers", "list"),
        new("logs", "DockerLogs", "DockerGroupContainers", "file-text", RequiresContainer: true),
        new("stats", "DockerResources", "DockerGroupContainers", "activity", RequiresContainer: true),
        new("details", "DockerDetails", "DockerGroupContainers", "info", RequiresContainer: true),
        new("start", "DockerStart", "DockerGroupContainers", "play", true, true),
        new("stop", "DockerStop", "DockerGroupContainers", "square", true, true),
        new("restart", "DockerRestart", "DockerGroupContainers", "rotate-cw", true, true),
        new("images", "DockerImages", "DockerGroupImages", "layers"),
        new("pull", "DockerPull", "DockerGroupImages", "download", true),
        new("compose-ps", "DockerComposePs", "DockerGroupCompose", "container", RequiresCompose: true),
        new("compose-up", "DockerComposeUp", "DockerGroupCompose", "play", true, RequiresCompose: true),
        new("compose-restart", "DockerComposeRestart", "DockerGroupCompose", "rotate-cw", true, RequiresCompose: true),
        new("compose-logs", "DockerComposeLogs", "DockerGroupCompose", "file-text", RequiresCompose: true),
    ]);

    /// <summary>只接受目录中已知操作；功能缺失直接阻止操作，不猜测备用变更命令。</summary>
    public static SshDockerCommandPlan Build(string operationId, SshDockerCommandOptions options, SshDockerEnvironment environment)
    {
        var operation = Operations.FirstOrDefault(o => o.Id == operationId) ?? throw Text.Exception("DockerInvalidParameters");
        if (!environment.Installed) throw Text.Exception("SshMonitorDockerMissing");
        if (!environment.CanAccess) throw Text.Exception("DockerDaemonUnavailable");
        if (!environment.SupportsBaseCommands) throw Text.Exception("DockerVersionUnsupported");
        if (operation.RequiresCompose && environment.ComposeCommand.Length > 0 && !environment.SupportsComposeCommands) throw Text.Exception("DockerComposeVersionUnsupported");
        if (operation.RequiresContainer) SshDockerStatsReader.ValidateId(options.ContainerId);
        if (operationId == "stats" && !environment.SupportsStats) throw Text.Exception("DockerStatsUnsupported");
        bool logs = operationId is "logs" or "compose-logs";
        if (logs && (options.TailLines is not (100 or 500 or 1000) || options.Since is not ("" or "10m" or "1h" or "24h"))) throw Text.Exception("DockerInvalidParameters");
        if (operationId is "stop" or "restart" && options.TimeoutSeconds is < 1 or > 300) throw Text.Exception("DockerInvalidParameters");
        string range = logs ? " --tail " + options.TailLines.ToString(CultureInfo.InvariantCulture) + (options.Since.Length > 0 ? " --since " + options.Since : "") +
            (options.Timestamps ? " --timestamps" : "") + (options.Follow ? " --follow" : "") : "";
        string command = operationId switch
        {
            "containers" => "docker ps -a --no-trunc",
            "logs" => "docker logs" + range + " " + options.ContainerId,
            "stats" => SshDockerStatsReader.Command([options.ContainerId]),
            "details" => "docker inspect --format '{{json .State}}' " + options.ContainerId,
            "start" => "docker start " + options.ContainerId,
            "stop" or "restart" => "docker " + operationId + " --time " + options.TimeoutSeconds.ToString(CultureInfo.InvariantCulture) + " " + options.ContainerId,
            "images" => "docker images --no-trunc",
            "pull" => "docker pull " + Image(options.Image),
            _ => Compose(operation, options, environment, range),
        };
        return new(command, operation, logs && options.Follow);
    }

    private static string Compose(SshDockerCommandDefinition operation, SshDockerCommandOptions options, SshDockerEnvironment environment, string range)
    {
        if (environment.ComposeCommand is not ("docker compose" or "docker-compose")) throw Text.Exception("DockerComposeMissing");
        if (!options.ProjectDirectory.StartsWith('/') || options.ProjectDirectory.Length > 4096 || options.ProjectDirectory.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(options.ComposeFile) || options.ComposeFile.Length > 4096 || options.ComposeFile.Any(char.IsControl)) throw Text.Exception("DockerComposePathInvalid");
        if (options.Service.Length > 0 && !Regex.IsMatch(options.Service, @"\A[a-zA-Z0-9][a-zA-Z0-9_.\-]{0,127}\z", RegexOptions.CultureInvariant)) throw Text.Exception("DockerInvalidParameters");
        string verb = operation.Id switch { "compose-ps" => "ps", "compose-up" => "up -d --no-build", "compose-restart" => "restart", "compose-logs" => "logs" + range, _ => throw Text.Exception("DockerInvalidParameters") };
        return "cd " + Quote(options.ProjectDirectory) + " && " + environment.ComposeCommand + " -f " + Quote(options.ComposeFile) + " " + verb +
            (options.Service.Length > 0 ? " " + Quote(options.Service) : "");
    }
    private static string Image(string value) => Regex.IsMatch(value, @"\A[a-zA-Z0-9][a-zA-Z0-9._:/@\-]{0,255}\z", RegexOptions.CultureInvariant)
        ? Quote(value) : throw Text.Exception("DockerImageInvalid");
    private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
