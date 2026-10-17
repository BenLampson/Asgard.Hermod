using System.Collections.Frozen;
using System.IO;

namespace Asgard.Hermod.UI.Services;

/// <summary>内置常见类型映射，仅使用名称和已有元数据，不下载文件或改变打开策略。</summary>
public static class FileTypeCatalog
{
    private static readonly FileTypeInfo Unknown = new("file", "TextMutedBrush", "FileTypeUnknown");
    private static readonly FileTypeInfo Folder = new("folder", "WarningBrush", "FileTypeFolder");
    private static readonly (FileTypeInfo Type, string Extensions)[] Groups =
    [
        (new("video", "FileVideoBrush", "FileTypeVideo"), "mp4 mov mkv avi webm flv m4v wmv mpg mpeg"),
        (new("music", "FileAudioBrush", "FileTypeAudio"), "mp3 wav flac aac ogg m4a wma opus aiff mid"),
        (new("file-image", "SuccessBrush", "FileTypeImage"), "png jpg jpeg gif webp bmp tif tiff ico heic avif"),
        (new("pen-tool", "FileDesignBrush", "FileTypeDesign"), "svg ai eps psd sketch fig xd cdr afdesign"),
        (new("file-text", "DangerBrush", "FileTypePdf"), "pdf"),
        (new("file-text", "AccentBrush", "FileTypeDocument"), "doc docx odt rtf pages wps"),
        (new("sheet", "SuccessBrush", "FileTypeSpreadsheet"), "xls xlsx csv tsv ods numbers et"),
        (new("presentation", "FilePresentationBrush", "FileTypePresentation"), "ppt pptx odp key dps"),
        (new("file-text", "TextMutedBrush", "FileTypeText"), "txt md markdown log rst adoc tex"),
        (new("file-archive", "WarningBrush", "FileTypeArchive"), "zip rar 7z tar gz bz2 xz tgz zst tar.gz tar.bz2 tar.xz tar.zst"),
        (new("file-code", "FileCodeBrush", "FileTypeCode"), "js ts jsx tsx py cs java go rs c cpp h php"),
        (new("code", "FileCodeBrush", "FileTypeWeb"), "html htm css scss sass less vue svelte"),
        (new("braces", "FileDataBrush", "FileTypeData"), "json jsonc yaml yml xml toml ini conf config env"),
        (new("database", "FileCodeBrush", "FileTypeDatabase"), "sql db sqlite sqlite3 mdb accdb parquet"),
        (new("type", "FileVideoBrush", "FileTypeFont"), "ttf otf woff woff2 eot"),
        (new("app-window", "TextMutedBrush", "FileTypeApplication"), "exe msi msix apk dmg pkg deb rpm"),
        (new("terminal", "TextMutedBrush", "FileTypeScript"), "sh bash zsh ps1 bat cmd"),
        (new("file-key", "FileDesignBrush", "FileTypeCertificate"), "pem crt cer pfx p12 pub p7b"),
        (new("disc-3", "TextMutedBrush", "FileTypeDiskImage"), "iso img vhd vhdx vmdk"),
    ];
    private static readonly FrozenDictionary<string, FileTypeInfo> Extensions = Groups
        .SelectMany(group => group.Extensions.Split(' ').Select(extension => KeyValuePair.Create(extension, group.Type)))
        .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>扩展名忽略大小写，先匹配复合后缀；仅无后缀时使用已读取的 Content-Type。</summary>
    public static FileTypeInfo Resolve(string key, bool directory = false, string? contentType = null)
    {
        if (directory) return Folder;
        string name = key[(key.LastIndexOf('/') + 1)..];
        foreach (string suffix in new[] { "tar.gz", "tar.bz2", "tar.xz", "tar.zst" })
            if (name.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                return Extensions[suffix] with { Extension = suffix };
        // 项目常用无后缀文本文件不被父目录中的句点误判。
        if (new[] { "README", "LICENSE", "Dockerfile", "Makefile", ".gitignore", ".editorconfig" }
            .Contains(name, StringComparer.OrdinalIgnoreCase))
            return Extensions[name.StartsWith('.') ? "env" : "txt"];
        string extension = name.Equals(".env", StringComparison.OrdinalIgnoreCase) ? "env" : Path.GetExtension(name).TrimStart('.');
        if (extension.Length > 0)
            return Extensions.TryGetValue(extension, out var type) ? type with { Extension = extension } : Unknown;
        string mime = contentType?.Split(';')[0].Trim().ToLowerInvariant() ?? "";
        string? mimeExtension = mime switch
        {
            "application/pdf" => "pdf",
            "application/json" => "json",
            "application/xml" or "text/xml" => "xml",
            "text/csv" => "csv",
            "text/tab-separated-values" => "tsv",
            "text/html" => "html",
            "text/css" => "css",
            "image/svg+xml" => "svg",
            "application/zip" or "application/x-7z-compressed" or "application/x-rar-compressed"
                or "application/vnd.rar" or "application/gzip" or "application/x-tar" => "zip",
            "application/msword" or "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
            "application/vnd.ms-excel" or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
            "application/vnd.ms-powerpoint" or "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "pptx",
            _ when mime.StartsWith("video/", StringComparison.Ordinal) => "mp4",
            _ when mime.StartsWith("audio/", StringComparison.Ordinal) => "mp3",
            _ when mime.StartsWith("image/", StringComparison.Ordinal) => "png",
            _ when mime.StartsWith("font/", StringComparison.Ordinal) => "ttf",
            _ when mime.StartsWith("text/", StringComparison.Ordinal) => "txt",
            _ => null
        };
        return mimeExtension is null ? Unknown : Extensions[mimeExtension];
    }
}
