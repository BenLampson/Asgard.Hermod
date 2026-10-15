namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>最近两分钟真实采样的图表高度，缺失 CPU 差分用零高度表示不绘制。</summary>
public sealed record SshMonitorSample(double CpuHeight, double MemoryHeight);
