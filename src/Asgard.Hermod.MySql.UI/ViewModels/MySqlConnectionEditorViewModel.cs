namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>只提供字段分区，公共向导拥有认证测试、标签和保存状态。</summary>
public sealed class MySqlConnectionEditorViewModel : ObservableObject, IDisposable
{
    private bool _attached;
    private readonly HashSet<InstanceConfigurationFieldViewModel> _observed = [];
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public MySqlConnectionEditorViewModel(NewInstanceViewModel wizard) { Wizard = wizard; Attach(); }
    /// <summary>公共向导拥有校验、测试、标签和持久化流程。</summary>
    public NewInstanceViewModel Wizard { get; }
    /// <summary>连接字段分区的稳定标识与本地化标题。</summary>
    public IReadOnlyList<InstanceFieldChoice> Panes => Wizard.SelectedProvider?.Id == MySqlIds.Ssh
        ? [new("connection", Text.Get("MySqlConnectionDetails")), new("security", Text.Get("MySqlSecurity")), new("ssh", Text.Get("MySqlSsh")), new("advanced", Text.Get("MySqlAdvanced"))]
        : [new("connection", Text.Get("MySqlConnectionDetails")), new("security", Text.Get("MySqlSecurity")), new("advanced", Text.Get("MySqlAdvanced"))];
    /// <summary>当前分区；切换不丢失输入。</summary>
    public string Pane { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(Fields)); } } = "connection";
    /// <summary>适用于当前分区的公共字段视图模型。</summary>
    public IReadOnlyList<InstanceConfigurationFieldViewModel> Fields => Wizard.Fields.Where(item => Pane switch
    {
        "connection" => item.Key is "host" or "port" or "user" or "password" or "database",
        "advanced" => item.Key is "timeout" or "commandTimeout",
        "ssh" => item.Key.StartsWith("ssh.", StringComparison.Ordinal),
        _ => item.Key is not ("host" or "port" or "user" or "password" or "database" or "timeout" or "commandTimeout") && !item.Key.StartsWith("ssh.", StringComparison.Ordinal),
    }).ToArray();
    /// <summary>当前是否选择了不验证身份或明文策略。</summary>
    public bool HasTlsWarning => Wizard.Fields.FirstOrDefault(item => item.Key == "tls")?.Value is "required" or "disabled";
    private void OnWizardChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(Fields)); OnPropertyChanged(nameof(HasTlsWarning));
        if (args.PropertyName == nameof(NewInstanceViewModel.SelectedProvider)) OnPropertyChanged(nameof(Panes));
        if (args.PropertyName == nameof(NewInstanceViewModel.Fields)) ObserveFields();
    }
    /// <summary>恢复公共向导状态监听。</summary>
    public void Attach() { if (_attached) return; _attached = true; Wizard.PropertyChanged += OnWizardChanged; ObserveFields(); }
    private void ObserveFields()
    {
        foreach (var item in _observed) item.PropertyChanged -= OnFieldChanged;
        _observed.Clear();
        if (!_attached) return;
        foreach (var item in Wizard.Fields) { _observed.Add(item); item.PropertyChanged += OnFieldChanged; }
    }
    private void OnFieldChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(InstanceConfigurationFieldViewModel.Value)) OnPropertyChanged(nameof(HasTlsWarning)); }
    /// <summary>解除公共向导和字段的状态监听。</summary>
    public void Dispose() { _attached = false; Wizard.PropertyChanged -= OnWizardChanged; ObserveFields(); }
}
