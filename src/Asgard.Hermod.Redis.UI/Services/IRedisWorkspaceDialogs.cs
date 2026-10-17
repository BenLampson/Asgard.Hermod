using Asgard.Hermod.Redis.UI.ViewModels;

namespace Asgard.Hermod.Redis.UI.Services;

/// <summary>业务确认可在测试中替换；模态窗口使用公共外观及动作栏。</summary>
public interface IRedisWorkspaceDialogs
{
    /// <summary>保存关闭、放弃关闭或继续编辑。</summary>
    Task<RedisCloseChoice> CloseDirtyAsync(string key, int database);
    /// <summary>删除展示完整键和数据库。</summary>
    Task<bool> DeleteAsync(string key, int database);
    /// <summary>展示从当前操作固定的实例和环境上下文，兼容无窗口实现。</summary>
    Task<bool> DeleteAsync(string key, int database, string target) => DeleteAsync(key, database);
    /// <summary>新建使用 create-if-missing，不覆盖既有数据。</summary>
    Task<RedisKeyId?> CreateAsync(IRedisSession session, int database, string prefix, CancellationToken cancellationToken = default);
    /// <summary>创建目标绑定当前操作，后续树或数据库选择不能改变它。</summary>
    Task<RedisKeyId?> CreateAsync(IRedisSession session, int database, string prefix, string target,
        CancellationToken cancellationToken = default) => CreateAsync(session, database, prefix, cancellationToken);
    /// <summary>环境与名称跟随同一稳定实例，数据库在打开时固定。</summary>
    Task<RedisKeyId?> CreateAsync(IRedisSession session, int database, string prefix, InstanceItemViewModel instance,
        CancellationToken cancellationToken = default) => CreateAsync(session, database, prefix,
            Text.Format("RedisWriteTarget", instance.Name, instance.EnvironmentLabel, database), cancellationToken);
    /// <summary>已有键在公共模态窗口中编辑，内容页保持只读。</summary>
    Task EditAsync(RedisKeyTabViewModel tab, bool append = false);
    /// <summary>空文本为永久，正数为秒。</summary>
    Task<string?> ExpiryAsync(string key);
    /// <summary>对比最新读取结果时保留草稿。</summary>
    Task ShowLatestAsync(string key, string preview);
}
