namespace Asgard.Hermod.UI.Services;

/// <summary>通过注册的页面提供者扩展工作区，避免在外壳写入第三方判断。</summary>
public sealed class InstanceWorkspaceFactory : IInstanceWorkspaceFactory
{
    private readonly IReadOnlyDictionary<string, IInstancePageProvider> _providers;

    /// <summary>实例类型必须唯一注册，重复配置在启动时明确失败。</summary>
    public InstanceWorkspaceFactory(IEnumerable<IInstancePageProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToDictionary(provider => provider.TypeId, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public WorkspacePageViewModel? CreatePage(InstanceItemViewModel instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.Summary.HasConfiguration && _providers.TryGetValue(instance.Summary.TypeId, out var provider)
            ? provider.CreatePage(instance) : null;
    }
}
