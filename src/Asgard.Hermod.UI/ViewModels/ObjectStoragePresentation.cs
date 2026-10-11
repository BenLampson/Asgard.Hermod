using System.Globalization;
using System.IO;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>集中格式化对象信息和安全错误，不将 SDK 原始消息放入界面。</summary>
internal static class ObjectStoragePresentation
{
    /// <summary>按二进制单位展示本页对象大小，不推算整个容器容量。</summary>
    internal static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>以用户本地时间呈现对象更新时间。</summary>
    internal static string Modified(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—";

    /// <summary>仅显示文件名，目录保留结尾的斜杠。</summary>
    internal static string Name(string key) => key.TrimEnd('/').Split('/').LastOrDefault() + (key.EndsWith('/') ? "/" : "");

    /// <summary>通过扩展名选择官方图标，文件内容不会参与执行。</summary>
    internal static string Icon(string key, bool directory = false) => directory ? "folder" : Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".json" => "file-json",
        ".zip" or ".gz" or ".7z" or ".tar" => "file-archive",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => "image",
        _ => "file-text"
    };

    /// <summary>常见存储类型使用设计稿的中文名称。</summary>
    internal static string StorageClass(string value) => value switch
    {
        "Standard" => "标准存储",
        "IA" => "低频访问",
        "Archive" => "归档存储",
        "ColdArchive" => "冷归档存储",
        "DeepColdArchive" => "深度冷归档",
        _ => value
    };

    /// <summary>未知异常使用固定文案，已清理的适配器错误才允许展示消息。</summary>
    internal static string Error(Exception exception, string fallback) => exception is ObjectStorageException safe
        ? safe.Message + (string.IsNullOrWhiteSpace(safe.ErrorCode) ? "" : "（" + safe.ErrorCode + "）") : fallback;
}
