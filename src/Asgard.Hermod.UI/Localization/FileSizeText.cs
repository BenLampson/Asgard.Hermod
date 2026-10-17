namespace Asgard.Hermod.UI.Localization;

/// <summary>公共文件占用显示使用共享整句模板，区域数字格式独立于界面语言。</summary>
public static class FileSizeText
{
    /// <summary>以 1024 为换算基数，在设置页面和确认弹窗中保持相同精度。</summary>
    public static string Format(long bytes) => bytes switch
    {
        < 1024 => Text.Format("StorageSizeBytes", bytes),
        < 1024 * 1024 => Text.Format("StorageSizeKilobytes", bytes / 1024d),
        < 1024L * 1024 * 1024 => Text.Format("StorageSizeMegabytes", bytes / (1024d * 1024)),
        _ => Text.Format("StorageSizeGigabytes", bytes / (1024d * 1024 * 1024))
    };
}
