using System.IO;

namespace Asgard.Hermod.UI.Services;

/// <summary>公共文件类型和预览上限，文件名不作为脚本或富文本执行。</summary>
public static class FilePreviewPolicy
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico" };
    private static readonly HashSet<string> TextFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".markdown", ".json", ".xml", ".yaml", ".yml", ".csv", ".tsv",
        ".ini", ".conf", ".config", ".toml", ".env", ".sql", ".cs", ".js", ".ts", ".jsx", ".tsx",
        ".css", ".scss", ".html", ".htm", ".svg", ".py", ".sh", ".ps1", ".bat", ".cmd", ".cpp", ".h", ".java"
    };

    /// <summary>图片下载预览最多 20 MiB。</summary>
    public const long ImageLimit = 20 * 1024 * 1024;
    /// <summary>文本窗口最多 2 MiB；正文依旧可以下载到本地。</summary>
    public const long TextLimit = 2 * 1024 * 1024;
    /// <summary>WPF 内置解码器支持的图片格式。</summary>
    public static bool IsImage(string name) => Images.Contains(Path.GetExtension(name));
    /// <summary>常见文本扩展名，以及无扩展名的标准项目文件。</summary>
    public static bool IsText(string name) => TextFiles.Contains(Path.GetExtension(name))
        || Path.GetFileName(name).Equals(".env", StringComparison.OrdinalIgnoreCase)
        || new[] { "README", "LICENSE", "Dockerfile", "Makefile", ".gitignore", ".editorconfig" }
            .Contains(Path.GetFileName(name), StringComparer.OrdinalIgnoreCase);
    /// <summary>返回可预览文件的字节上限，零表示不支持。</summary>
    public static long Limit(string name) => IsImage(name) ? ImageLimit : IsText(name) ? TextLimit : 0;
}
