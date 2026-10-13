namespace Asgard.Hermod.UI.ViewModels;

/// <summary>首页按连接器类型呈现实例数量，类型标识不参与翻译。</summary>
public sealed record HomeInstanceTypeViewModel(string Id, string Name, string Icon, int Count)
{
    /// <summary>完整的本地化计数，便于辅助技术读取。</summary>
    public string Summary => Text.Format("HomeTypeCount", Name, Count);
}
