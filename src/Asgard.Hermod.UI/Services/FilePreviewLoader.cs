using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Services;

/// <summary>后台解码受限文件，避免大图片或二进制内容阻塞 WPF。</summary>
public static class FilePreviewLoader
{
    /// <summary>完整读取后再次核对大小，支持 UTF-8 和带 BOM 的 UTF-16/32。</summary>
    public static Task<FilePreviewContent> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            long limit = FilePreviewPolicy.Limit(path);
            if (limit == 0) throw Text.Exception("FilePreviewUnsupported");
            using var stream = File.OpenRead(path);
            if (stream.Length > limit) throw Text.Exception("FilePreviewTooLarge");
            cancellationToken.ThrowIfCancellationRequested();
            if (FilePreviewPolicy.IsImage(path))
                return DecodeImage(stream, path, cancellationToken);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            string text = reader.ReadToEnd();
            cancellationToken.ThrowIfCancellationRequested();
            if (text.Contains('\0')) throw Text.Exception("FilePreviewNotText");
            return new FilePreviewContent(Path.GetFileName(path), text, null);
        }, cancellationToken);

    /// <summary>用户明确选择图片时按正文解码，扩展名不决定类型，但保留图片大小与尺寸上限。</summary>
    public static Task<FilePreviewContent> LoadImageAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(path);
            if (stream.Length > FilePreviewPolicy.ImageLimit) throw Text.Exception("FilePreviewTooLarge");
            try { return DecodeImage(stream, path, cancellationToken); }
            catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException
                or System.Runtime.InteropServices.COMException)
            {
                throw Text.Exception("ForcePreviewDecodeFailed");
            }
        }, cancellationToken);

    /// <summary>先检查原始尺寸再缩采样，冻结图片并脱离文件流，避免窗口生命周期占用缓存文件。</summary>
    private static FilePreviewContent DecodeImage(Stream stream, string path, CancellationToken cancellationToken)
    {
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        if ((long)frame.PixelWidth * frame.PixelHeight > 100_000_000 || frame.PixelWidth > 65536 || frame.PixelHeight > 65536)
            throw Text.Exception("FilePreviewTooLarge");
        double scale = Math.Min(1, 4096d / Math.Max(frame.PixelWidth, frame.PixelHeight));
        int width = Math.Max(1, (int)(frame.PixelWidth * scale));
        int height = Math.Max(1, (int)(frame.PixelHeight * scale));
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = width;
        image.DecodePixelHeight = height;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        cancellationToken.ThrowIfCancellationRequested();
        // 扩展名来自已验证的解码器，使未知扩展名图片也能调用正确的系统关联。
        string extension = frame.Decoder switch
        {
            JpegBitmapDecoder => ".jpg",
            BmpBitmapDecoder => ".bmp",
            GifBitmapDecoder => ".gif",
            TiffBitmapDecoder => ".tiff",
            IconBitmapDecoder => ".ico",
            PngBitmapDecoder => ".png",
            _ => throw Text.Exception("ForcePreviewDecodeFailed"),
        };
        return new(Path.GetFileName(path), "", image, extension);
    }
}
