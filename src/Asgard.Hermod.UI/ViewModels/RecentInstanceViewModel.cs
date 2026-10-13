namespace Asgard.Hermod.UI.ViewModels;

/// <summary>使用当前安全快照解析最近访问，改名后不显示旧名称。</summary>
public sealed record RecentInstanceViewModel(InstanceItemViewModel Instance, DateTimeOffset OpenedAt)
{
    /// <summary>区域格式独立于界面语言，持久化始终保存 UTC 时间。</summary>
    public string OpenedAtText
    {
        get
        {
            var local = OpenedAt.ToLocalTime();
            var today = DateTimeOffset.UtcNow.ToLocalTime().Date;
            return (today - local.Date).Days switch
            {
                0 => Text.Format("HomeOpenedToday", local.ToString("t")),
                1 => Text.Format("HomeOpenedYesterday", local.ToString("t")),
                _ => local.ToString("g")
            };
        }
    }
    /// <summary>最近条目的完整辅助标签。</summary>
    public string AccessibleName => Text.Format("HomeRecentAccessible", Instance.DisplayName, OpenedAtText);
}
