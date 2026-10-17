namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>稳定键驱动的表单字段，翻译不参与业务判断。</summary>
public sealed class RabbitMqDraftField(string key, string resource, string value = "", string kind = "text", bool required = false) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = Text.Get(resource);
    public string Kind { get; } = kind;
    public bool Required { get; } = required;
    public bool IsReadOnly { get; init; }
    public string Value { get; set => SetProperty(ref field, value); } = value;
    public bool Checked { get => Value == "true"; set { Value = value ? "true" : "false"; OnPropertyChanged(); } }
    /// <summary>布尔选择的稳定值不随语言变化。</summary>
    public IReadOnlyList<InstanceFieldChoice> Choices { get; init; } = kind == "boolean"
        ? [new("true", Text.Get("RabbitMqYes")), new("false", Text.Get("RabbitMqNo"))] : [];
}
