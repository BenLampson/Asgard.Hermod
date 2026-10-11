namespace Asgard.Hermod.UI.Services;

/// <summary>由宿主提供创建实例的交互入口。</summary>
public interface IInstanceEditor
{
    /// <summary>取消时返回 null；本阶段仅创建当前工作区的内存配置。</summary>
    Task<InstanceSummary?> CreateAsync();
}
