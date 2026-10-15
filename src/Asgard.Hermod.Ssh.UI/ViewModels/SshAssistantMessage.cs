using System.Text.RegularExpressions;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>原始对话内容和明确标记的 shell 建议；普通解释不可作为命令执行。</summary>
public sealed partial class SshAssistantMessage
{
    /// <summary>区分用户、助手与原始命令结果。</summary>
    public SshAssistantMessage(string content, bool user, long revision, bool extractCommands = true)
    {
        Content = content; Role = Text.Get(user ? "SshAgentYou" : "SshAgentTitle");
        Commands = user || !extractCommands ? [] : ShellBlocks().Matches(content).Select(match => match.Groups[1].Value.Trim()).Where(value => value.Length is > 0 and <= 16384).Take(8).Select(value => new SshAssistantCommand(value, revision)).ToArray();
    }
    /// <summary>完整回复。</summary>
    public string Content { get; }
    /// <summary>角色名资源。</summary>
    public string Role { get; }
    /// <summary>建议命令最多八项。</summary>
    public IReadOnlyList<SshAssistantCommand> Commands { get; }
    [GeneratedRegex(@"(?ms)^```(?:bash|sh|shell|shellscript)[ \t]*\r?\n(.*?)^```[ \t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ShellBlocks();
}
