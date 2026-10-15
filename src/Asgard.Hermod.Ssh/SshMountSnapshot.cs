namespace Asgard.Hermod.Ssh;

/// <summary>按字节表示的挂载容量，tmpfs 等内存文件系统不计入持久分区汇总。</summary>
public sealed record SshMountSnapshot(string Device, string Path, string FileSystem, long TotalBytes, long UsedBytes, long AvailableBytes)
{
    /// <summary>使用率根据原始容量计算，不依赖显示字符串。</summary>
    public double UsedPercent => TotalBytes > 0 ? Math.Clamp(100d * UsedBytes / TotalBytes, 0, 100) : 0;
    /// <summary>只有块设备计入持久分区，避免把 overlay 和 tmpfs 重复相加。</summary>
    public bool IsPersistent => Device.StartsWith("/dev/", StringComparison.Ordinal);
}
