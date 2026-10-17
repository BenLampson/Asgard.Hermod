using Asgard.Hermod.UI.Localization;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>进程行显示值与排序值分离，不展示可能包含凭据的参数。</summary>
public sealed record SshMonitorProcessItem(SshProcessSnapshot Value)
{
    /// <summary>稳定进程 ID。</summary>
    public int Pid => Value.Pid;
    /// <summary>远端进程名称原样显示。</summary>
    public string Name => Value.Name;
    /// <summary>远端用户原样显示。</summary>
    public string User => Value.User;
    /// <summary>CPU 数值供排序。</summary>
    public double CpuPercent => Value.CpuPercent;
    /// <summary>内存占比数值供排序。</summary>
    public double MemoryPercent => Value.MemoryPercent;
    /// <summary>物理内存字节数供排序。</summary>
    public long ResidentBytes => Value.ResidentBytes;
    /// <summary>CPU 百分比区域格式。</summary>
    public string Cpu => Text.Format("SshMonitorPercent", CpuPercent);
    /// <summary>内存百分比区域格式。</summary>
    public string Memory => Text.Format("SshMonitorPercent", MemoryPercent);
    /// <summary>物理内存格式。</summary>
    public string Resident => FileSizeText.Format(ResidentBytes);
    /// <summary>状态按协议首字符映射，未知值不当作运行。</summary>
    public string Status => Text.Get(Value.State.FirstOrDefault() switch
    {
        'R' => "SshMonitorRunning",
        'S' or 'D' or 'I' => "SshMonitorSleeping",
        'Z' => "SshMonitorZombie",
        'T' or 't' => "SshMonitorStopped",
        _ => "SshMonitorNoData",
    });
}
