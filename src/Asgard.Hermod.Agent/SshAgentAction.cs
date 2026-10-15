namespace Asgard.Hermod.Agent;

/// <summary>已经通过固定模板校验的动作；RequiresConfirmation 为真时必须确认完整命令。</summary>
public sealed record SshAgentAction(string Operation, string Command, bool RequiresConfirmation)
{
    /// <summary>所有执行通道从根目录开始，命令整体在独立子 Shell 中运行，不继承终端的目录或环境。</summary>
    public string ExecutionCommand => "cd / && sh -c '" + Command.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
