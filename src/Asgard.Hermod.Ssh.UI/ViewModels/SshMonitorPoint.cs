namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>图表单点的真实时间与可空读数。</summary>
public sealed record SshMonitorPoint(DateTimeOffset Timestamp, double? Value);
