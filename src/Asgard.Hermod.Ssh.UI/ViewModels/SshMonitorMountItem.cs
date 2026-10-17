using Asgard.Hermod.UI.Localization;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>挂载容量使用原始字节值排序，格式仅参与显示。</summary>
public sealed record SshMonitorMountItem(SshMountSnapshot Value)
{
    /// <summary>设备原值。</summary>
    public string Device => Value.Device;
    /// <summary>挂载路径原值。</summary>
    public string Path => Value.Path;
    /// <summary>文件系统协议标识。</summary>
    public string FileSystem => Value.FileSystem;
    /// <summary>总量原始字节。</summary>
    public long TotalBytes => Value.TotalBytes;
    /// <summary>已用原始字节。</summary>
    public long UsedBytes => Value.UsedBytes;
    /// <summary>可用原始字节。</summary>
    public long AvailableBytes => Value.AvailableBytes;
    /// <summary>使用率数值。</summary>
    public double UsedPercent => Value.UsedPercent;
    /// <summary>容量高于设计提示阈值。</summary>
    public bool IsHighUsage => UsedPercent > 85;
    /// <summary>总量显示。</summary>
    public string Total => FileSizeText.Format(TotalBytes);
    /// <summary>已用显示。</summary>
    public string Used => FileSizeText.Format(UsedBytes);
    /// <summary>可用显示。</summary>
    public string Available => FileSizeText.Format(AvailableBytes);
    /// <summary>百分比显示。</summary>
    public string Percent => Text.Format("SshMonitorPercent", UsedPercent);
}
