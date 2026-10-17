namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>命令绑定生成时的连接代次，重连后不能应用旧建议。</summary>
public sealed record SshAssistantCommand(string Command, long Revision);
