using System.Text.RegularExpressions;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>可选择的消息与独立工具步骤；原始命令输出不能被提取为可执行建议。</summary>
public sealed partial class SshAssistantMessage : AgentDisplayMessage
{
    /// <summary>区分用户、助手与工具记录，保留连接代次供命令应用时校验。</summary>
    public SshAssistantMessage(string content, bool user, long revision, bool extractCommands = true, string? roleKey = null, bool isTool = false, bool isError = false, string recoveryPrompt = "", string modelIdentity = "", AgentApprovalRequest? approval = null) : base(content, user, isTool, isError, recoveryPrompt, modelIdentity, approval)
    {
        Role = Text.Get(roleKey ?? (user ? "SshAgentYou" : "SshAgentTitle"));
        Commands = user || !extractCommands ? [] : ShellBlocks().Matches(content).Select(match => match.Groups[1].Value.Trim()).Where(value => value.Length is > 0 and <= 8192).Take(8).Select(value => new SshAssistantCommand(value, revision)).ToArray();
        if (Commands.Count > 0) Content = ShellBlocks().Replace(content, "").Trim();
    }
    /// <summary>角色或工具步骤标题资源。</summary>
    public override string Role { get; }
    /// <summary>建议命令最多八项。</summary>
    public IReadOnlyList<SshAssistantCommand> Commands { get; }
    /// <summary>更新同一条执行记录，避免运行中与完成后出现重复步骤。</summary>
    internal void Update(string content, string statusKey) => UpdateMessage(content, statusKey);
    [GeneratedRegex(@"(?ms)^```(?:bash|sh|shell|shellscript)[ \t]*\r?\n(.*?)^```[ \t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ShellBlocks();
}
