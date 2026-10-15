namespace Asgard.Hermod.Ssh;

/// <summary>进程快照保留数值供排序；CPU 为 ps 生命周期平均值，按主机逻辑核心数归一。</summary>
public sealed record SshProcessSnapshot(int Pid, string Name, string User, string State, double CpuPercent, double MemoryPercent, long ResidentBytes);
