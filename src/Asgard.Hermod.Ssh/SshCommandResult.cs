namespace Asgard.Hermod.Ssh;

/// <summary>终端进程原始输出不翻译；退出码与截断状态独立于显示文案。</summary>
public sealed record SshCommandResult(string Output, int ExitCode, bool Truncated);
