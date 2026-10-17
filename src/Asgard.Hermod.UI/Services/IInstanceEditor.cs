namespace Asgard.Hermod.UI.Services;

/// <summary>由宿主提供创建实例的交互入口。</summary>
public interface IInstanceEditor
{
    /// <summary>成功保存后返回安全快照，取消或未保存时返回 null。</summary>
    Task<InstanceSummary?> CreateAsync();
    /// <summary>编辑已经保存的实例，取消时保留原配置。</summary>
    Task<InstanceSummary?> EditAsync(string instanceId) => Task.FromResult<InstanceSummary?>(null);
    /// <summary>编辑名称、托管品牌和标签，无需读取或测试连接。</summary>
    Task<InstanceSummary?> RenameAsync(string instanceId) => Task.FromResult<InstanceSummary?>(null);
    /// <summary>只替换稳定标签关联，取消不落库。</summary>
    Task<InstanceSummary?> EditTagsAsync(string instanceId) => Task.FromResult<InstanceSummary?>(null);
}
