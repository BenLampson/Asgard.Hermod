using Asgard.Hermod.Redis.UI.ViewModels;

namespace Asgard.Hermod.Redis.UI.Services;

/// <summary>业务确认可在测试中替换；模态窗口使用公共外观及动作栏。</summary>
public interface IRedisWorkspaceDialogs
{
    /// <summary>保存关闭、放弃关闭或继续编辑。</summary>
    Task<RedisCloseChoice> CloseDirtyAsync(string key, int database);
    /// <summary>删除展示完整键和数据库。</summary>
    Task<bool> DeleteAsync(string key, int database);
    /// <summary>新建使用 create-if-missing，不覆盖既有数据。</summary>
    Task<RedisCreateRequest?> CreateAsync();
    /// <summary>空文本为永久，正数为秒。</summary>
    Task<string?> ExpiryAsync(string key);
    /// <summary>对比最新读取结果时保留草稿。</summary>
    Task ShowLatestAsync(string key, string preview);
}
