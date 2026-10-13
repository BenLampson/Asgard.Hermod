namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>只适配公共字段与分区布局，不复制向导保存及校验。</summary>
public sealed class RedisConnectionEditorViewModel : ObservableObject, IDisposable
{
    private bool _attached;
    /// <summary>复用公共向导的字段状态。</summary>
    public RedisConnectionEditorViewModel(NewInstanceViewModel wizard) { Wizard = wizard; Attach(); }
    /// <summary>实例名、测试与保存由公共向导提供。</summary>
    public NewInstanceViewModel Wizard { get; }
    /// <summary>字段分区使用稳定 ID。</summary>
    public IReadOnlyList<InstanceFieldChoice> Panes { get; } = [new("connection", Text.Get("RedisConnectionInformation")),
        new("security", Text.Get("RedisSecurityInformation")), new("route", Text.Get("RedisRouteInformation"))];
    /// <summary>切换分区保留仍适用的输入。</summary>
    public string Pane { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(Fields)); } } = "connection";
    /// <summary>隐藏字段仍由公共向导计算适用性；本适配器不持有额外凭据。</summary>
    public IReadOnlyList<InstanceConfigurationFieldViewModel> Fields => Wizard.Fields.Where(item => Pane switch
    {
        "connection" => item.Key is "host" or "port" or "nodes" or "service" or "database",
        "route" => item.Key is "route" or "timeout" || item.Key.StartsWith("ssh.", StringComparison.Ordinal) || item.Key.StartsWith("proxy.", StringComparison.Ordinal),
        _ => item.Key is not ("host" or "port" or "nodes" or "service" or "database" or "route" or "timeout")
            && !item.Key.StartsWith("ssh.", StringComparison.Ordinal) && !item.Key.StartsWith("proxy.", StringComparison.Ordinal),
    }).ToArray();
    private void OnWizardChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(NewInstanceViewModel.Fields) or nameof(NewInstanceViewModel.VisibleFields)) OnPropertyChanged(nameof(Fields)); }
    /// <summary>公共向导缓存视图重新激活时恢复监听。</summary>
    public void Attach() { if (_attached) return; _attached = true; Wizard.PropertyChanged += OnWizardChanged; }
    /// <inheritdoc />
    public void Dispose() { _attached = false; Wizard.PropertyChanged -= OnWizardChanged; }
}
