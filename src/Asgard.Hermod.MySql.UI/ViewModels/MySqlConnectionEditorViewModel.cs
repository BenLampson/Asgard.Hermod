namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>只提供字段分区，公共向导拥有认证测试、标签和保存状态。</summary>
public sealed class MySqlConnectionEditorViewModel : ObservableObject, IDisposable
{
    private bool _attached;
    public MySqlConnectionEditorViewModel(NewInstanceViewModel wizard) { Wizard = wizard; Attach(); }
    public NewInstanceViewModel Wizard { get; }
    public IReadOnlyList<InstanceFieldChoice> Panes => Wizard.SelectedProvider?.Id == MySqlIds.Ssh
        ? [new("connection", Text.Get("MySqlConnectionDetails")), new("security", Text.Get("MySqlSecurity")), new("ssh", Text.Get("MySqlSsh")), new("advanced", Text.Get("MySqlAdvanced"))]
        : [new("connection", Text.Get("MySqlConnectionDetails")), new("security", Text.Get("MySqlSecurity")), new("advanced", Text.Get("MySqlAdvanced"))];
    public string Pane { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(Fields)); } } = "connection";
    public IReadOnlyList<InstanceConfigurationFieldViewModel> Fields => Wizard.Fields.Where(item => Pane switch
    {
        "connection" => item.Key is "host" or "port" or "user" or "password" or "database",
        "advanced" => item.Key is "timeout" or "commandTimeout",
        "ssh" => item.Key.StartsWith("ssh.", StringComparison.Ordinal),
        _ => item.Key is not ("host" or "port" or "user" or "password" or "database" or "timeout" or "commandTimeout") && !item.Key.StartsWith("ssh.", StringComparison.Ordinal),
    }).ToArray();
    public bool HasTlsWarning => Wizard.Fields.FirstOrDefault(item => item.Key == "tls")?.Value is "required" or "disabled";
    private void OnWizardChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(Fields)); OnPropertyChanged(nameof(HasTlsWarning));
        if (args.PropertyName == nameof(NewInstanceViewModel.SelectedProvider)) OnPropertyChanged(nameof(Panes));
    }
    public void Attach() { if (_attached) return; _attached = true; Wizard.PropertyChanged += OnWizardChanged; foreach (var item in Wizard.Fields) item.PropertyChanged += OnFieldChanged; }
    private void OnFieldChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(InstanceConfigurationFieldViewModel.Value)) OnPropertyChanged(nameof(HasTlsWarning)); }
    public void Dispose() { _attached = false; Wizard.PropertyChanged -= OnWizardChanged; foreach (var item in Wizard.Fields) item.PropertyChanged -= OnFieldChanged; }
}
