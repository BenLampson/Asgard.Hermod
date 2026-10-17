namespace Asgard.Hermod.UI.Tests;

/// <summary>验证类型识别的类别、大小写、后缀边界及安全回退，预览策略保持独立。</summary>
public sealed class FileTypeCatalogTests
{
    /// <summary>各类常见文件和复合后缀都有稳定的图标及类别资源键。</summary>
    [Theory]
    [InlineData("uploads/video/storyboard/clip.MP4", "video", "FileTypeVideo")]
    [InlineData("clip.mov", "video", "FileTypeVideo")]
    [InlineData("clip.webm", "video", "FileTypeVideo")]
    [InlineData("voice.FLAC", "music", "FileTypeAudio")]
    [InlineData("cover.avif", "file-image", "FileTypeImage")]
    [InlineData("cover.HEIC", "file-image", "FileTypeImage")]
    [InlineData("logo.svg", "pen-tool", "FileTypeDesign")]
    [InlineData("layout.psd", "pen-tool", "FileTypeDesign")]
    [InlineData("brief.pdf", "file-text", "FileTypePdf")]
    [InlineData("outline.docx", "file-text", "FileTypeDocument")]
    [InlineData("shots.xlsx", "sheet", "FileTypeSpreadsheet")]
    [InlineData("shots.csv", "sheet", "FileTypeSpreadsheet")]
    [InlineData("slides.pptx", "presentation", "FileTypePresentation")]
    [InlineData("slides.key", "presentation", "FileTypePresentation")]
    [InlineData("README.md", "file-text", "FileTypeText")]
    [InlineData("assets.rar", "file-archive", "FileTypeArchive")]
    [InlineData("assets.TAR.GZ", "file-archive", "FileTypeArchive")]
    [InlineData("assets.tar.xz", "file-archive", "FileTypeArchive")]
    [InlineData("main.rs", "file-code", "FileTypeCode")]
    [InlineData("index.tsx", "file-code", "FileTypeCode")]
    [InlineData("index.html", "code", "FileTypeWeb")]
    [InlineData("theme.scss", "code", "FileTypeWeb")]
    [InlineData("settings.yaml", "braces", "FileTypeData")]
    [InlineData("settings.jsonc", "braces", "FileTypeData")]
    [InlineData("project/.env", "braces", "FileTypeData")]
    [InlineData("store.sqlite3", "database", "FileTypeDatabase")]
    [InlineData("inter.woff2", "type", "FileTypeFont")]
    [InlineData("setup.msix", "app-window", "FileTypeApplication")]
    [InlineData("build.ps1", "terminal", "FileTypeScript")]
    [InlineData("certificate.pfx", "file-key", "FileTypeCertificate")]
    [InlineData("disk.vhdx", "disc-3", "FileTypeDiskImage")]
    [InlineData("unknown.extension", "file", "FileTypeUnknown")]
    [InlineData("clip.mp4.bak", "file", "FileTypeUnknown")]
    [InlineData("folder.mp4/no-extension", "file", "FileTypeUnknown")]
    [InlineData("", "file", "FileTypeUnknown")]
    [InlineData("README", "file-text", "FileTypeText")]
    [InlineData("project/.gitignore", "braces", "FileTypeData")]
    public void Resolve_RecognizesCommonTypesAndSuffixBoundaries(string key, string icon, string label)
    {
        var type = FileTypeCatalog.Resolve(key);
        Assert.Equal(icon, type.Icon);
        Assert.Equal(label, type.LabelKey);
    }

    /// <summary>目录优先于扩展名，元数据仅补充无后缀文件且不虚构扩展名。</summary>
    [Theory]
    [InlineData("clip.mp4/", true, "video/mp4", "folder")]
    [InlineData("stream", false, " VIDEO/MP4 ; codecs=avc1", "video")]
    [InlineData("recording", false, "audio/ogg", "music")]
    [InlineData("image", false, "image/png", "file-image")]
    [InlineData("logo", false, "image/svg+xml", "pen-tool")]
    [InlineData("data", false, "application/json", "braces")]
    [InlineData("brief", false, "application/pdf", "file-text")]
    [InlineData("unknown", false, "application/octet-stream", "file")]
    [InlineData("clip.mp4.bak", false, "video/mp4", "file")]
    [InlineData("clip.mp4", false, "text/plain", "video")]
    public void Resolve_UsesDirectoryAndExtensionBeforeContentType(string key, bool directory, string mime, string icon)
    {
        var type = FileTypeCatalog.Resolve(key, directory, mime);
        Assert.Equal(icon, type.Icon);
        if (!key.Contains('.')) Assert.Empty(type.Extension);
    }

    /// <summary>识别扩展名不改变现有预览及执行边界。</summary>
    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("cover.avif")]
    [InlineData("outline.docx")]
    [InlineData("setup.exe")]
    public void IconRecognition_DoesNotGrantPreviewSupport(string key)
    {
        Assert.NotEqual("file", FileTypeCatalog.Resolve(key).Icon);
        Assert.Equal(0, FilePreviewPolicy.Limit(key));
    }

    /// <summary>行模型与上传队列使用同一套映射。</summary>
    [Fact]
    public void Mp4_RowAndUploadQueueUseVideoIcon()
    {
        var row = new ObjectStorageEntryViewModel(new("clip.MP4", false), () => { });
        var queue = new ObjectStorageUploadQueueItemViewModel(new("clip.mp4", "clip.mp4", 10));
        Assert.Equal("video", row.Icon);
        Assert.Equal("video", queue.Icon);
        Assert.Equal("FileVideoBrush", row.IconBrushKey);
    }
}
