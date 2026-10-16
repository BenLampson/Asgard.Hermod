namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>分区只投影公共字段，不保存第二份连接参数。</summary>
public sealed class RabbitMqConnectionEditorViewModel : InstanceConfigurationEditorViewModel
{
    /// <summary>挂接公共向导。</summary>
    public RabbitMqConnectionEditorViewModel(NewInstanceViewModel wizard) : base(wizard) => Attach();
    /// <summary>分区和原稿顺序一致。</summary>
    public IReadOnlyList<InstanceFieldChoice> Panes => Wizard.SelectedProvider?.Id == RabbitMqIds.Ssh
        ? [new("connection", Text.Get("ConnectionInformation")), new("ssh", Text.Get("RabbitMqSsh")), new("tls", Text.Get("RabbitMqTls")), new("advanced", Text.Get("RabbitMqAdvanced"))]
        : [new("connection", Text.Get("ConnectionInformation")), new("tls", Text.Get("RabbitMqTls")), new("advanced", Text.Get("RabbitMqAdvanced"))];
    /// <summary>切换只影响可见字段。</summary>
    public string Pane { get; set { if (SetProperty(ref field, value)) RefreshFields(); } } = "connection";
    /// <summary>HTTP 明文提示始终对应实际协议。</summary>
    public bool IsPlaintext => GetField("scheme").Value == "http";
    /// <inheritdoc />
    protected override bool IncludesField(InstanceConfigurationFieldViewModel item) => Pane switch
    {
        "connection" => item.Key is "scheme" or "host" or "port" or "user" or "password",
        "ssh" => item.Key.StartsWith("ssh.", StringComparison.Ordinal),
        "tls" => item.Key is "trust" or "ca",
        _ => item.Key is "prefix" or "timeout",
    };
    /// <inheritdoc />
    protected override void OnFieldsChanged() { OnPropertyChanged(nameof(Panes)); OnPropertyChanged(nameof(IsPlaintext)); }
    /// <inheritdoc />
    protected override void OnFieldValueChanged(InstanceConfigurationFieldViewModel item)
    {
        if (item.Key != "scheme") return;
        var port = GetField("port");
        if (port.Value is "15671" or "15672") port.Value = item.Value == "https" ? "15671" : "15672";
        OnPropertyChanged(nameof(IsPlaintext));
    }
}
