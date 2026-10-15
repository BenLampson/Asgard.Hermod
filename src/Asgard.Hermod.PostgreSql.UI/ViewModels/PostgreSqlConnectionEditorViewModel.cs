namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>只提供字段分区，公共向导拥有认证测试、标签和保存状态。</summary>
public sealed class PostgreSqlConnectionEditorViewModel : InstanceConfigurationEditorViewModel
{
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public PostgreSqlConnectionEditorViewModel(NewInstanceViewModel wizard) : base(wizard) => Attach();
    /// <summary>连接字段分区的稳定标识与本地化标题。</summary>
    public IReadOnlyList<InstanceFieldChoice> Panes => Wizard.SelectedProvider?.Id == PostgreSqlIds.Ssh
        ? [new("connection", Text.Get("PostgreSqlConnectionDetails")), new("security", Text.Get("PostgreSqlSecurity")), new("ssh", Text.Get("PostgreSqlSsh")), new("advanced", Text.Get("PostgreSqlAdvanced"))]
        : [new("connection", Text.Get("PostgreSqlConnectionDetails")), new("security", Text.Get("PostgreSqlSecurity")), new("advanced", Text.Get("PostgreSqlAdvanced"))];
    /// <summary>当前分区；切换不丢失输入。</summary>
    public string Pane { get; set { if (SetProperty(ref field, value)) RefreshFields(); } } = "connection";
    /// <summary>当前是否选择了不验证身份或明文策略。</summary>
    public bool HasTlsWarning => Wizard.Fields.FirstOrDefault(item => item.Key == "tls")?.Value is "required" or "disabled";
    /// <summary>服务商字段更换后只更新模块自己的选项和提示。</summary>
    protected override void OnFieldsChanged()
    {
        OnPropertyChanged(nameof(Panes));
        OnPropertyChanged(nameof(HasTlsWarning));
    }
    /// <summary>模块仅声明分区成员，集合更新由公共层完成。</summary>
    protected override bool IncludesField(InstanceConfigurationFieldViewModel item) => Pane switch
    {
        "connection" => item.Key is "host" or "port" or "user" or "password" or "database",
        "advanced" => item.Key is "timeout" or "commandTimeout",
        "ssh" => item.Key.StartsWith("ssh.", StringComparison.Ordinal),
        _ => item.Key is not ("host" or "port" or "user" or "password" or "database" or "timeout" or "commandTimeout") && !item.Key.StartsWith("ssh.", StringComparison.Ordinal),
    };
    /// <summary>TLS 提示只依赖 TLS 字段值，不重新提供表单集合。</summary>
    protected override void OnFieldValueChanged(InstanceConfigurationFieldViewModel item)
    {
        if (item.Key == "tls") OnPropertyChanged(nameof(HasTlsWarning));
    }
}
