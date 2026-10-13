namespace Asgard.Hermod.UI.ViewModels;

/// <summary>供展开列表和折叠导航共同使用的实例状态。</summary>
public sealed class InstanceItemViewModel : ObservableObject
{
    private bool _isSelected;
    private readonly IInstanceProviderRegistry? _registry;
    private ConfigurationSyncStatus _syncStatus = ConfigurationSyncStatus.Local;

    /// <summary>创建实例导航条目。</summary>
    public InstanceItemViewModel(InstanceSummary instance, IInstanceProviderRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        Summary = instance;
        _registry = registry;
    }

    /// <summary>保留应用层提供的实例快照。</summary>
    public InstanceSummary Summary { get; private set; }
    /// <summary>实例稳定标识。</summary>
    public string Id => Summary.Id;
    /// <summary>实例名称。</summary>
    public string Name => Summary.Name;
    /// <summary>详情和页签标题。</summary>
    public string DisplayName => Summary.DisplayName;
    /// <summary>矢量图标名称。</summary>
    public string Icon => Summary.Icon;
    /// <summary>类型来自连接器注册表，品牌图标不能替代类型图标。</summary>
    public string TypeIcon => Type?.Icon ?? (IsStorage ? "database" : IsLocal ? "terminal" :
        Summary.HasConfiguration ? "app-window" : Icon);
    /// <summary>未安装连接器时保持安全的通用类型说明。</summary>
    public string TypeName => Type?.Name ?? Text.Get(IsStorage ? "Storage" : IsLocal ? "LocalTerminal" : "HomeOtherInstances");
    /// <summary>同一类型的所有服务商共用相同类型元数据。</summary>
    private InstanceTypeDescriptor? Type => _registry?.Types.FirstOrDefault(type => type.Id == Summary.TypeId);
    /// <summary>品牌独立呈现；未配置实例及重复的通用图标不占用品牌位置。</summary>
    public bool HasProviderIcon => Summary.HasConfiguration && Icon != TypeIcon;
    /// <summary>安全导航快照包含服务商显示名称，不读取连接参数。</summary>
    public string ProviderName => string.IsNullOrWhiteSpace(Summary.Endpoint) ? Summary.ProviderId : Summary.Endpoint;
    /// <summary>类型图标的完整本地化提示。</summary>
    public string TypeHint => Text.Format("InstanceTypeHint", TypeName);
    /// <summary>品牌图标的完整本地化提示。</summary>
    public string ProviderHint => Text.Format("InstanceProviderHint", ProviderName);
    /// <summary>本地模式与内置终端没有云端同步标记。</summary>
    public bool HasSyncStatus => !IsLocal && _syncStatus != ConfigurationSyncStatus.Local;
    /// <summary>当前配置与最近确认的云端内容或成功基准的比较结果。</summary>
    public ConfigurationSyncStatus SyncStatus => _syncStatus;
    /// <summary>已同步、待同步、进行中和需要处理使用不同图形。</summary>
    public string SyncIcon => _syncStatus switch
    {
        ConfigurationSyncStatus.Synced => "cloud-check",
        ConfigurationSyncStatus.Synchronizing => "refresh-cw",
        ConfigurationSyncStatus.Conflict or ConfigurationSyncStatus.Failed => "circle-alert",
        _ => "cloud-upload",
    };
    /// <summary>提示只描述配置同步，不代表服务连接成功。</summary>
    public string SyncHint => Text.Get("InstanceSync" + _syncStatus);
    /// <summary>折叠导航与辅助技术仍能读取名称、类型、品牌和同步结果。</summary>
    public string AccessibleName => HasSyncStatus
        ? Text.Format("InstanceNavigationCloudInfo", Name, TypeName, ProviderName, SyncHint)
        : Text.Format(HasProviderIcon ? "InstanceNavigationInfo" : "InstanceNavigationTypeInfo", Name, TypeName, ProviderName);

    /// <summary>同步事件只更新显示状态，保持导航对象与当前选中态。</summary>
    public void UpdateSyncStatus(ConfigurationSyncStatus status)
    {
        if (_syncStatus == status) return;
        _syncStatus = status;
        foreach (string property in new[] { nameof(SyncStatus), nameof(HasSyncStatus), nameof(SyncIcon), nameof(SyncHint), nameof(AccessibleName) })
            OnPropertyChanged(property);
    }
    /// <summary>存储类型共用设计中的琥珀色图标，不绑定具体服务商。</summary>
    public bool IsStorage => Summary.TypeId == "storage";
    /// <summary>过滤标签。</summary>
    public IReadOnlyList<string> Tags => Summary.Tags;
    /// <summary>是否可以打开本地终端。</summary>
    public bool IsLocal => Summary.IsLocal;
    /// <summary>是否展示异常提示。</summary>
    public bool HasAlert => Summary.HasAlert;
    /// <summary>当前工作页是否属于该实例。</summary>
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>刷新安全快照并保留导航选中态和正在使用的条目对象。</summary>
    public void UpdateSnapshot(InstanceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.Id != Id)
            throw new InvalidOperationException("An instance snapshot must keep its identifier.");
        Summary = summary;
        foreach (string property in new[] { nameof(Summary), nameof(Name), nameof(DisplayName),
            nameof(Icon), nameof(IsStorage), nameof(Tags), nameof(IsLocal), nameof(HasAlert), nameof(TypeIcon),
            nameof(TypeName), nameof(HasProviderIcon), nameof(ProviderName), nameof(TypeHint), nameof(ProviderHint),
            nameof(HasSyncStatus), nameof(AccessibleName) })
            OnPropertyChanged(property);
    }
}
