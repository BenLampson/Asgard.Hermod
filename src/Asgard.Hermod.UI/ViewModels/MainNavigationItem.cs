namespace Asgard.Hermod.UI.ViewModels;

/// <summary>固定页签以稳定类型标识选择，不把本地化标题作为逻辑条件。</summary>
public sealed class MainNavigationItem(string id, string title, string icon, string description = "") : ObservableObject
{
    /// <summary>概览、全部实例或类型 ID。</summary>
    public string Id { get; } = id;
    /// <summary>已从共享资源或类型注册表读取的标题。</summary>
    public string Title { get; } = title;
    /// <summary>公共图标标识。</summary>
    public string Icon { get; } = icon;
    /// <summary>类型入口的完整说明。</summary>
    public string Description { get; } = description;
    /// <summary>真实配置数量。</summary>
    public int Count { get; internal set => SetProperty(ref field, value); }
}
