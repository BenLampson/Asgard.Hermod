using System.Text.RegularExpressions;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>可选择的消息与独立工具步骤；原始命令输出不能被提取为可执行建议。</summary>
public sealed partial class SshAssistantMessage : ObservableObject
{
    /// <summary>区分用户、助手与工具记录，保留连接代次供命令应用时校验。</summary>
    public SshAssistantMessage(string content, bool user, long revision, bool extractCommands = true, string? roleKey = null, bool isTool = false)
    {
        Content = content; IsUser = user; IsTool = isTool; Role = Text.Get(roleKey ?? (user ? "SshAgentYou" : "SshAgentTitle"));
        Commands = user || !extractCommands ? [] : ShellBlocks().Matches(content).Select(match => match.Groups[1].Value.Trim()).Where(value => value.Length is > 0 and <= 8192).Take(8).Select(value => new SshAssistantCommand(value, revision)).ToArray();
        if (Commands.Count > 0) Content = ShellBlocks().Replace(content, "").Trim();
    }
    /// <summary>模型原文或命令执行预览和结果。</summary>
    public string Content { get; private set => SetProperty(ref field, value); }
    /// <summary>用户消息使用设计稿中的浅色底，助手回复保持独立文本布局。</summary>
    public bool IsUser { get; }
    /// <summary>工具步骤拥有独立边框和状态，不提供插入或重新执行入口。</summary>
    public bool IsTool { get; }
    /// <summary>只有模型回复解析 Markdown，用户输入和原始工具输出保持字面内容。</summary>
    public bool IsAssistant => !IsUser && !IsTool;
    /// <summary>角色或工具步骤标题资源。</summary>
    public string Role { get; }
    /// <summary>当前工具步骤的本地化状态。</summary>
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>建议命令最多八项。</summary>
    public IReadOnlyList<SshAssistantCommand> Commands { get; }
    /// <summary>更新同一条执行记录，避免运行中与完成后出现重复步骤。</summary>
    internal void Update(string content, string statusKey) { Content = content; Status = Text.Get(statusKey); }
    [GeneratedRegex(@"(?ms)^```(?:bash|sh|shell|shellscript)[ \t]*\r?\n(.*?)^```[ \t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ShellBlocks();
}
