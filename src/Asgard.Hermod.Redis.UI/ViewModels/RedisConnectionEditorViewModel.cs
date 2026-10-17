namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>只适配公共字段与分区布局，不复制向导保存及校验。</summary>
public sealed class RedisConnectionEditorViewModel : InstanceConfigurationEditorViewModel
{
    /// <summary>复用公共向导的字段状态。</summary>
    public RedisConnectionEditorViewModel(NewInstanceViewModel wizard) : base(wizard) => Attach();
    /// <summary>字段分区使用稳定 ID。</summary>
    public IReadOnlyList<InstanceFieldChoice> Panes { get; } = [new("connection", Text.Get("RedisConnectionInformation")),
        new("security", Text.Get("RedisSecurityInformation")), new("route", Text.Get("RedisRouteInformation"))];
    /// <summary>切换分区保留仍适用的输入。</summary>
    public string Pane { get; set { if (SetProperty(ref field, value)) RefreshFields(); } } = "connection";
    /// <summary>模块仅声明分区成员，集合更新与条件可见性由公共层完成。</summary>
    protected override bool IncludesField(InstanceConfigurationFieldViewModel item) => Pane switch
    {
        "connection" => item.Key is "host" or "port" or "nodes" or "service" or "database",
        "route" => item.Key is "route" or "timeout" || item.Key.StartsWith("ssh.", StringComparison.Ordinal) || item.Key.StartsWith("proxy.", StringComparison.Ordinal),
        _ => item.Key is not ("host" or "port" or "nodes" or "service" or "database" or "route" or "timeout")
            && !item.Key.StartsWith("ssh.", StringComparison.Ordinal) && !item.Key.StartsWith("proxy.", StringComparison.Ordinal),
    };
}
