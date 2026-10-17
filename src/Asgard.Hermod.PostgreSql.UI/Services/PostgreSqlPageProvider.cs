using Asgard.Hermod.Agent;
using Asgard.Hermod.PostgreSql.UI.ViewModels;

namespace Asgard.Hermod.PostgreSql.UI.Services;

/// <summary>通过模块扩展点提供页面，公共外壳无需识别 PostgreSQL 类型。</summary>
public sealed class PostgreSqlPageProvider(IInstanceConfigurationService configurations, IPostgreSqlSessionFactory factory, IInstanceEditor editor, IPostgreSqlWorkspaceDialogs dialogs, IAgentClient? agentClient = null) : IInstancePageProvider
{
    /// <summary>公共注册表使用的稳定实例类型标识。</summary>
    public string TypeId => PostgreSqlIds.Type;
    /// <summary>创建由公共页签生命周期管理的 PostgreSQL 页面。</summary>
    public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance) => new PostgreSqlWorkspaceViewModel(instance, configurations, factory, editor, dialogs, agentClient: agentClient);
}
