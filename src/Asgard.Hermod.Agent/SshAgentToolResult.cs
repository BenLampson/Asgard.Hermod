namespace Asgard.Hermod.Agent;

/// <summary>工具结果与执行状态分离；未知结果或拒绝后关闭本轮工具权限，防止自动重试。</summary>
public sealed record SshAgentToolResult(string Status, string Output, int? ExitCode = null, bool Truncated = false, bool StopActions = false);
