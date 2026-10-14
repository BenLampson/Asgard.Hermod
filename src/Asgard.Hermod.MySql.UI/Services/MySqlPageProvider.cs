using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Services;

/// <summary>通过模块扩展点提供页面，公共外壳无需识别 MySQL 类型。</summary>
public sealed class MySqlPageProvider(IInstanceConfigurationService configurations, IMySqlSessionFactory factory, IInstanceEditor editor, IMySqlWorkspaceDialogs dialogs) : IInstancePageProvider
{
    public string TypeId => MySqlIds.Type;
    public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance) => new MySqlWorkspaceViewModel(instance, configurations, factory, editor, dialogs);
}
