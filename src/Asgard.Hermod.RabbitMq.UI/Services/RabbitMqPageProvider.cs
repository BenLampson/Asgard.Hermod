using Asgard.Hermod.Agent;
using Asgard.Hermod.RabbitMq.UI.ViewModels;

namespace Asgard.Hermod.RabbitMq.UI.Services;

/// <summary>通过公共工作区生命周期创建 RabbitMQ 页面。</summary>
public sealed class RabbitMqPageProvider(IInstanceConfigurationService configurations, IRabbitMqSessionFactory factory,
    IInstanceEditor editor, IRabbitMqDialogs dialogs, IAgentClient? agent = null) : IInstancePageProvider
{
    /// <inheritdoc />
    public string TypeId => RabbitMqIds.Type;
    /// <inheritdoc />
    public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance) => new RabbitMqWorkspaceViewModel(instance, configurations, factory, editor, dialogs, agent);
}
