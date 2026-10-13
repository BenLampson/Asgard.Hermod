using System.Globalization;

namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

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

    /// <summary>上传队列与对象列表共享内置扩展名映射。</summary>
    internal static string Icon(string key, bool directory = false) => FileTypeCatalog.Resolve(key, directory).Icon;

    /// <summary>存储类型协议值映射到当前界面语言。</summary>
    internal static string StorageClass(string value) => value switch
    {
        "Standard" => Text.Get("StandardStorage"),
        "IA" => Text.Get("InfrequentAccess"),
        "Archive" => Text.Get("ArchiveStorage"),
        "ColdArchive" => Text.Get("ColdArchiveStorage"),
        "DeepColdArchive" => Text.Get("DeepColdArchive"),
        _ => value
    };

    /// <summary>未知异常使用固定文案，已清理的适配器错误才允许展示消息。</summary>
    internal static string Error(Exception exception, string fallback)
    {
        if (exception.Data["Hermod.ResourceKey"] is not string) return fallback;
        var message = Text.Error(exception);
        return exception is ObjectStorageException { ErrorCode: { Length: > 0 } code }
            ? Text.Format("MessageWithErrorCode", message, code) : message;
    }
}
