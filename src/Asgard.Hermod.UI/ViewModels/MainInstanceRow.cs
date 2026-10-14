namespace Asgard.Hermod.UI.ViewModels;

/// <summary>表格只组合安全实例快照与本机访问时间，不读取凭据或猜测在线状态。</summary>
public sealed record MainInstanceRow(InstanceItemViewModel Instance, DateTimeOffset? OpenedAt)
{
    /// <summary>名称下方的安全协议地址。</summary>
    public string Address => Instance.Summary.ConnectionAddress.Length > 0 ? Instance.Summary.ConnectionAddress : Text.Get("MainAddressUnavailable");
    /// <summary>服务品牌与自部署身份独立于配置保存位置。</summary>
    public bool IsSelfHosted => Instance.Summary.TypeId is "mysql" or "redis" or "ssh";
    /// <summary>提供商列仅使用品牌或自部署标签。</summary>
    public string ProviderName => IsSelfHosted ? Text.Get("Local") : Instance.ProviderName;
    /// <summary>本地电脑标识不代表配置存于本地。</summary>
    public string ProviderIcon => IsSelfHosted ? "monitor" : Instance.Icon;
    /// <summary>相对时间遵循界面语言，数字日期仍遵循用户区域。</summary>
    public string OpenedAtText => OpenedAt is { } time ? new RecentInstanceViewModel(Instance, time).OpenedAtText : Text.Get("MainNeverOpened");
    /// <summary>标签保留用户原文，仅翻译内置标识。</summary>
    public IReadOnlyList<MainTagBadge> Tags => Instance.Tags.Select(path => new MainTagBadge(
        Localization.TagDisplayText.Name(path, path.Split('/')[^1]), Localization.TagDisplayText.FullPath(path),
        Instance.Summary.TagIds.Contains("builtin.environment.production") && path == "Environment/Production")).ToArray();
}
