namespace Asgard.Hermod.UI.Services;

/// <summary>依据已保存的实例类型分派详情页面，未接入的类型返回空值。</summary>
public interface IInstanceWorkspaceFactory
{
    /// <summary>为配置完整的实例创建专属页面。</summary>
    WorkspacePageViewModel? CreatePage(InstanceItemViewModel instance);
}
