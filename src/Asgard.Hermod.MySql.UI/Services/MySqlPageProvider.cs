using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Services;

/// <summary>通过模块扩展点提供页面，公共外壳无需识别 MySQL 类型。</summary>
public sealed class MySqlPageProvider(IInstanceConfigurationService configurations, IMySqlSessionFactory factory, IInstanceEditor editor, IMySqlWorkspaceDialogs dialogs) : IInstancePageProvider
{
    /// <summary>公共注册表使用的稳定实例类型标识。</summary>
    public string TypeId => MySqlIds.Type;
    /// <summary>创建由公共页签生命周期管理的 MySQL 页面。</summary>
    public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance) => new MySqlWorkspaceViewModel(instance, configurations, factory, editor, dialogs);
}
