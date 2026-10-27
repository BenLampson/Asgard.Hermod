namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>一次显式环境加载的上下文快照；资源错误独立缓存，不能伪装成空列表。</summary>
internal sealed record KubernetesEnvironmentSnapshot(
    string DefaultNamespace,
    IReadOnlyList<string> Namespaces,
    string NamespaceWarning,
    IReadOnlyDictionary<string, (IReadOnlyList<SshKubernetesResource> Items, string Error)> Resources);
