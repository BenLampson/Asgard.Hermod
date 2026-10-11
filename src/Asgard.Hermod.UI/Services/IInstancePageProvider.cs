namespace Asgard.Hermod.UI.Services;

/// <summary>每种实例类型提供自己的工作页，宿主不需要判断具体的第三方服务商。</summary>
public interface IInstancePageProvider
{
    /// <summary>此页面处理的顶层实例类型，例如存储或 Docker。</summary>
    string TypeId { get; }
    /// <summary>创建尚未开始远程读取的工作页。</summary>
    WorkspacePageViewModel CreatePage(InstanceItemViewModel instance);
}
