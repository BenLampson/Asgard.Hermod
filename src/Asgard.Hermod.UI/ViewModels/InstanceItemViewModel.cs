namespace Asgard.Hermod.UI.ViewModels;

/// <summary>供展开列表和折叠导航共同使用的实例状态。</summary>
public sealed class InstanceItemViewModel : ObservableObject
{
    private bool _isSelected;

    /// <summary>创建实例导航条目。</summary>
    public InstanceItemViewModel(InstanceSummary instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        Summary = instance;
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
            nameof(Icon), nameof(IsStorage), nameof(Tags), nameof(IsLocal), nameof(HasAlert) })
            OnPropertyChanged(property);
    }
}
