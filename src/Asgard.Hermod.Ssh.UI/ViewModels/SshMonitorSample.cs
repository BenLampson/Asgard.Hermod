namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>采样保留真实时间与未知值，绘图不能将缺失数据补成零。</summary>
public sealed record SshMonitorSample(DateTimeOffset Timestamp, double? Cpu, double? Memory, double? Sent, double? Received, IReadOnlyList<SshNetworkSnapshot> Interfaces);
