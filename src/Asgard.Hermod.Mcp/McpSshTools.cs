using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.Mcp;

/// <summary>外部客户端只能选择固定诊断，服务名使用桌面授权时冻结的值。</summary>
internal sealed class McpSshTools(ISshConnectionFactory factory)
{
    internal async Task<object> RunAsync(string name, McpGrant grant, InstanceConnectionOptions connection, McpArguments args, CancellationToken token)
    {
        var (check, command) = Command(name, grant, args.String("check"));
        var result = await factory.ExecuteDiagnosticAsync(connection, command, token).ConfigureAwait(false);
        return new { check, result.ExitCode, output = McpJson.Clip(result.Output, 8192), truncated = result.Truncated || result.Output.Length > 8192 };
    }

    internal static (string Check, string Command) Command(string name, McpGrant grant, string check)
    {
        if (name == "hermod_ssh_service_logs" || check == "service_status")
        {
            if (!McpGrantRules.IsServiceName(grant.Resource)) throw Text.Exception("McpAccessDenied");
            if (name == "hermod_ssh_service_logs")
                return ("service_logs", $"LC_ALL=C journalctl --no-pager -n 50 -u '{grant.Resource}'");
            return (check, $"LC_ALL=C systemctl show --no-pager -p LoadState,ActiveState,SubState -- '{grant.Resource}'");
        }
        return check switch
        {
            "host_summary" => (check, "LC_ALL=C uptime"),
            "disk_usage" => (check, "LC_ALL=C df -h"),
            "memory" => (check, "LC_ALL=C free -m"),
            _ => throw Text.Exception("McpInvalidArguments"),
        };
    }
}
