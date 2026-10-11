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
    public InstanceSummary Summary { get; }
    /// <summary>实例稳定标识。</summary>
    public string Id => Summary.Id;
    /// <summary>实例名称。</summary>
    public string Name => Summary.Name;
    /// <summary>详情和页签标题。</summary>
    public string DisplayName => Summary.DisplayName;
    /// <summary>矢量图标名称。</summary>
    public string Icon => Summary.Icon;
    /// <summary>过滤标签。</summary>
    public IReadOnlyList<string> Tags => Summary.Tags;
    /// <summary>是否可以打开本地终端。</summary>
    public bool IsLocal => Summary.IsLocal;
    /// <summary>是否展示异常提示。</summary>
    public bool HasAlert => Summary.HasAlert;
    /// <summary>当前工作页是否属于该实例。</summary>
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}
