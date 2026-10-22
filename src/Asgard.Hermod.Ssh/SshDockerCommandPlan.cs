namespace Asgard.Hermod.Ssh;

/// <summary>一次生成的不可变执行计划；预览、确认和执行必须使用同一个字符串。</summary>
public sealed record SshDockerCommandPlan(string Command, SshDockerCommandDefinition Operation, bool RunsInTerminal);
