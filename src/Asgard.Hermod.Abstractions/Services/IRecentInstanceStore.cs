namespace Asgard.Hermod.Abstractions.Services;

/// <summary>保存当前设备上的最近访问，不参与账号工作区同步。</summary>
public interface IRecentInstanceStore
{
    /// <summary>读取按访问时间倒序排列的记录。</summary>
    IReadOnlyList<RecentInstance> Load();
    /// <summary>替换最近记录，调用方负责去重与限制条目数。</summary>
    void Save(IReadOnlyList<RecentInstance> instances);
}
