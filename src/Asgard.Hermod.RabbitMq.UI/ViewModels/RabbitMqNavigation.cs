namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>导航使用枚举身份，翻译只影响展示。</summary>
public sealed record RabbitMqNavigation(RabbitMqResource Resource, string Icon, string? LabelKey = null) : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>当前 vhost 的服务器总数；尚未读取时不伪造数字。</summary>
    public string Count { get; private set; } = "";
    /// <summary>更新已读取的资源数量。</summary>
    public void SetCount(int? count)
    {
        Count = count?.ToString("N0", CultureInfo.CurrentCulture) ?? "";
        PropertyChanged?.Invoke(this, new(nameof(Count)));
    }
    /// <summary>本地化名称。</summary>
    public string Label => Text.Get(LabelKey ?? "RabbitMq" + Resource);
}
