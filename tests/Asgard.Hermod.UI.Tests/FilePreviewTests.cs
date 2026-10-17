using System.Text;
using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实文件解码、类型限制和程序参数传递。</summary>
public sealed class FilePreviewTests
{
    /// <summary>常见文本与图片均可预览，压缩包留给外部程序。</summary>
    [Theory]
    [InlineData("a.PNG", true, true)]
    [InlineData("a.jpeg", true, true)]
    [InlineData("a.json", true, false)]
    [InlineData("README", true, false)]
    [InlineData(".env", true, false)]
    [InlineData("a.svg", true, false)]
    [InlineData("a.zip", false, false)]
    public void Policy_RecognizesCommonFiles(string name, bool preview, bool image)
    {
        Assert.Equal(preview, FilePreviewPolicy.Limit(name) > 0);
        Assert.Equal(image, FilePreviewPolicy.IsImage(name));
    }

    /// <summary>UTF-8 与带 BOM 的 UTF-16 内容只作为文本，即使文件是 HTML 也不执行。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Text_DecodesUnicodeAndPreservesMarkup(bool utf16)
    {
        using var environment = new LocalStorageTestEnvironment();
        Directory.CreateDirectory(environment.DirectoryPath);
        string path = Path.Combine(environment.DirectoryPath, "hello.html");
        string text = "<script>alert('你好 & world')</script>\r\n";
        await File.WriteAllTextAsync(path, text, utf16 ? Encoding.Unicode : new UTF8Encoding(false), TestContext.Current.CancellationToken);
        var content = await FilePreviewLoader.LoadAsync(path, TestContext.Current.CancellationToken);
        Assert.True(content.IsText);
        Assert.Equal(text, content.Text);
        Assert.Null(content.Image);
    }

    /// <summary>图片解码脱离文件流，允许预览关闭后清理原文件。</summary>
    [Fact]
    public async Task Image_DecodesWithoutUpscalingAndReleasesFile()
    {
        using var environment = new LocalStorageTestEnvironment();
        Directory.CreateDirectory(environment.DirectoryPath);
        string path = Path.Combine(environment.DirectoryPath, "image.png");
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255 }, 8);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        var content = await FilePreviewLoader.LoadAsync(path, TestContext.Current.CancellationToken);
        var image = Assert.IsAssignableFrom<BitmapSource>(content.Image);
        Assert.True(image.IsFrozen);
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    /// <summary>二进制正文和超限文件不能放入文本控件。</summary>
    [Fact]
    public async Task Text_RejectsBinaryAndOversizedFile()
    {
        using var environment = new LocalStorageTestEnvironment();
        Directory.CreateDirectory(environment.DirectoryPath);
        string path = Path.Combine(environment.DirectoryPath, "binary.txt");
        await File.WriteAllBytesAsync(path, [65, 0, 66], TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => FilePreviewLoader.LoadAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal("FilePreviewNotText", error.Data["Hermod.ResourceKey"]);
        using (var file = File.Create(path)) file.SetLength(FilePreviewPolicy.TextLimit + 1);
        error = await Assert.ThrowsAsync<InvalidOperationException>(() => FilePreviewLoader.LoadAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal("FilePreviewTooLarge", error.Data["Hermod.ResourceKey"]);
    }

    /// <summary>包含空格、Unicode 和命令元字符的文件路径只能成为一个独立参数。</summary>
    [Fact]
    public async Task Launch_UsesOneArgumentAndBlocksExecutableSystemFallback()
    {
        using var environment = new LocalStorageTestEnvironment();
        Directory.CreateDirectory(environment.DirectoryPath);
        string path = Path.Combine(environment.DirectoryPath, "hello & 用户 (1).txt");
        await File.WriteAllTextAsync(path, "text", TestContext.Current.CancellationToken);
        string program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
        var rule = new FileOpenRule("TXT", program);
        var info = FileProgramLaunch.CreateStartInfo(path, rule);
        Assert.Equal(program, info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal(path, Assert.Single(info.ArgumentList));
        Assert.Empty(info.Arguments);
        info = FileProgramLaunch.CreateStartInfo(path, null);
        Assert.True(info.UseShellExecute);
        Assert.Equal(path, info.FileName);
        string script = Path.Combine(environment.DirectoryPath, "script.cmd");
        await File.WriteAllTextAsync(script, "echo hello", TestContext.Current.CancellationToken);
        Assert.Equal("FileOpenExecutableBlocked", Assert.Throws<InvalidOperationException>(() =>
            FileProgramLaunch.CreateStartInfo(script, null)).Data["Hermod.ResourceKey"]);
        Assert.False(FileProgramLaunch.CreateStartInfo(script, new("cmd", program)).UseShellExecute);
    }

    /// <summary>不同来源的同名文件隔离，目录和保留设备名不能覆盖任意用户文件。</summary>
    [Theory]
    [InlineData("../../CON.txt", ".txt")]
    [InlineData("folder\\name.exe ", ".exe")]
    [InlineData("README", "")]
    public void Cache_IsolatesNamesAndPreservesSafeExtension(string name, string extension)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        using var firstFile = environment.Storage.CreateFile(name);
        using var secondFile = environment.Storage.CreateFile(name);
        string first = firstFile.Path;
        string second = secondFile.Path;
        Assert.NotEqual(Path.GetDirectoryName(first), Path.GetDirectoryName(second));
        Assert.Equal(extension, Path.GetExtension(first));
        Assert.DoesNotContain("..", Path.GetFileName(first));
        Assert.DoesNotContain("\\", Path.GetFileName(first));
        if (name == "README") Assert.True(FilePreviewPolicy.IsText(first));
    }
}
