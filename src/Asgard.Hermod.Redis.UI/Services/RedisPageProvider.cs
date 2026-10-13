using Asgard.Hermod.Redis.UI.ViewModels;

namespace Asgard.Hermod.Redis.UI.Services;

/// <summary>实例页由协议模块提供，公共外壳不感知 Redis。</summary>
public sealed class RedisPageProvider(IInstanceConfigurationService configurations, IRedisSessionFactory connections,
    IInstanceEditor editor, IRedisWorkspaceDialogs dialogs) : IInstancePageProvider
{
    /// <inheritdoc />
    public string TypeId => RedisIds.Type;
    /// <inheritdoc />
    public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance) => new RedisWorkspaceViewModel(instance, configurations, connections, editor, dialogs);
}
