namespace Asgard.Hermod.Ssh;

/// <summary>内置操作以稳定 ID 驱动，标题与分组均由共享资源渲染。</summary>
public sealed record SshDockerCommandDefinition(string Id, string TitleKey, string GroupKey, string Icon,
    bool ChangesState = false, bool RequiresContainer = false, bool RequiresCompose = false);
