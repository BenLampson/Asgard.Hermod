namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>目录显示只解析资源，不用翻译后的文字决定执行逻辑。</summary>
public sealed record SshDockerOperationItem(SshDockerCommandDefinition Definition)
{
    /// <summary>稳定操作标识。</summary>
    public string Id => Definition.Id;
    /// <summary>本地化的操作标题。</summary>
    public string Title => Text.Get(Definition.TitleKey);
    /// <summary>本地化的目录分组。</summary>
    public string Group => Text.Get(Definition.GroupKey);
    /// <summary>目录图标来自内置白名单。</summary>
    public string Icon => Definition.Icon;
}
