using Asgard.Hermod.UI.Localization;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>分类标识用于逻辑，名称与容量仅用于本地化显示。</summary>
public sealed record StorageUsageViewModel(TemporaryStorageUsage Usage)
{
    /// <summary>分类名称来自共享资源。</summary>
    public string Name => Text.Get(Usage.Category switch
    {
        TemporaryFileCategory.Download => "StorageTemporaryDownloads",
        TemporaryFileCategory.Cache => "StorageCachedFiles",
        _ => "StorageProcessingFiles"
    });
    /// <summary>真实容量，不使用设计稿示例数据。</summary>
    public string Size => FileSizeText.Format(Usage.Bytes);
}
