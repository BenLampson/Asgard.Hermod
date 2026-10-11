namespace Asgard.Hermod.UI.ViewModels;

/// <summary>将详情快照呈现给页面；预览阶段不会触发远程管理操作。</summary>
public sealed class ServiceDetailViewModel : WorkspacePageViewModel
{
    /// <summary>从实例导航和可选详情快照构建页面。</summary>
    public ServiceDetailViewModel(InstanceItemViewModel instance, ServiceSnapshot? snapshot)
        : base("instance:" + instance.Id, instance.DisplayName, instance.Icon, instance.Id, hasAlert: instance.HasAlert)
    {
        Name = snapshot?.Name ?? instance.DisplayName;
        Subtitle = snapshot?.Subtitle ?? (string.IsNullOrWhiteSpace(instance.Summary.Endpoint)
            ? "No connector configured" : instance.Summary.Endpoint);
        Status = snapshot?.Status ?? "Not configured";
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
    /// <summary>地址或未配置说明。</summary>
    public string Subtitle { get; }
    /// <summary>服务状态文字。</summary>
    public string Status { get; }
    /// <summary>状态语义色。</summary>
    public StatusTone StatusTone { get; }
    /// <summary>是否为 Pencil 提供的示例数据。</summary>
    public bool IsPreview { get; }
    /// <summary>是否存在可展示的详情。</summary>
    public bool HasDetails { get; }
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
