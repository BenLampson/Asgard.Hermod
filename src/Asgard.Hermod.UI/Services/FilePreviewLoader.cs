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
                return new FilePreviewContent(Path.GetFileName(path), "", image);
            }
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            string text = reader.ReadToEnd();
            cancellationToken.ThrowIfCancellationRequested();
            if (text.Contains('\0')) throw Text.Exception("FilePreviewNotText");
            return new FilePreviewContent(Path.GetFileName(path), text, null);
        }, cancellationToken);
}
