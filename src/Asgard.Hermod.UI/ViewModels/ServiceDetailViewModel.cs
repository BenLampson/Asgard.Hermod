namespace Asgard.Hermod.UI.ViewModels;

/// <summary>呈现详情快照或配置保存状态，缺少详情不代表实例没有配置。</summary>
public sealed class ServiceDetailViewModel : WorkspacePageViewModel
{
    /// <summary>从实例导航和可选详情快照构建页面。</summary>
    public ServiceDetailViewModel(InstanceItemViewModel instance, ServiceSnapshot? snapshot)
        : base("instance:" + instance.Id, instance.DisplayName, instance.Icon, instance.Id, hasAlert: instance.HasAlert)
    {
        HasConfiguration = instance.Summary.HasConfiguration;
        Name = snapshot?.Name ?? instance.DisplayName;
        Subtitle = snapshot?.Subtitle ?? (string.IsNullOrWhiteSpace(instance.Summary.Endpoint)
            ? (HasConfiguration ? "实例配置已保存" : "No connector configured") : instance.Summary.Endpoint);
        Status = snapshot?.Status ?? (HasConfiguration ? "已配置" : "Not configured");
        StatusTone = snapshot?.StatusTone ?? StatusTone.Muted;
        IsPreview = snapshot?.IsPreview ?? false;
        HasDetails = snapshot is not null;
        Metrics = snapshot?.Metrics ?? [];
        Configuration = snapshot?.Configuration ?? [];
        Events = snapshot?.Events ?? [];
        RestartCommand = new RelayCommand(_ => { }, _ => false);
        DeployCommand = new RelayCommand(_ => { }, _ => false);
    }

    /// <summary>详情名称。</summary>
    public string Name { get; }
    /// <summary>详情地址或持久化配置说明，不推断实时连接状态。</summary>
    public string Subtitle { get; }
    /// <summary>服务状态文字。</summary>
    public string Status { get; }
    /// <summary>状态语义色。</summary>
    public StatusTone StatusTone { get; }
    /// <summary>是否为 Pencil 提供的示例数据。</summary>
    public bool IsPreview { get; }
    /// <summary>是否存在可展示的详情。</summary>
    public bool HasDetails { get; }
    /// <summary>实例来源是否包含已经保存的类型与提供方配置。</summary>
    public bool HasConfiguration { get; }
    /// <summary>没有详情时区分已经保存的实例与未配置的旧导航条目。</summary>
    public string PlaceholderTitle => HasConfiguration ? "实例已配置" : "No connector configured";
    /// <summary>已保存实例说明详情尚未接入，不要求用户重复配置。</summary>
    public string PlaceholderDescription => HasConfiguration ? "实例详情页面待接入。"
        : "Add a service connection to view configuration, metrics and events.";
    /// <summary>四项指标卡片。</summary>
    public IReadOnlyList<MetricValue> Metrics { get; }
    /// <summary>运行配置。</summary>
    public IReadOnlyList<ConfigurationValue> Configuration { get; }
    /// <summary>最近事件。</summary>
    public IReadOnlyList<ServiceEvent> Events { get; }
    /// <summary>连接器接入后启用重启操作。</summary>
    public RelayCommand RestartCommand { get; }
    /// <summary>连接器接入后启用部署操作。</summary>
    public RelayCommand DeployCommand { get; }
}
