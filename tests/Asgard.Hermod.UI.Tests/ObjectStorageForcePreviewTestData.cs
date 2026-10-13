using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Tests;

/// <summary>强行预览测试使用真实的小 PNG 和隔离临时存储，不访问云端。</summary>
internal static class ObjectStorageForcePreviewTestData
{
    /// <summary>图片名称刻意不带可识别扩展名，验证实际内容解码。</summary>
    internal static ObjectStorageTestService Service(string name = "image.dat", string contentType = "application/octet-stream", long size = 839) => new()
    {
        ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new(name, false, size), new("other.dat", false, 100), new("folder/", true)])),
        DetailsHandler = (_, _, key, _) => Task.FromResult(new ObjectStorageObjectDetails(key, size, null, "Standard", contentType, "", PreviewMessage: "")),
        DownloadHandler = (_, path, _, token) => File.WriteAllBytesAsync(path, ImageBytes(), token),
    };

    /// <summary>使用已有工作区与临时存储边界，不打开真实外部程序。</summary>
    internal static ObjectStorageWorkspaceViewModel Model(ObjectStorageTestService service, ObjectStorageTestDialogs dialogs, ITemporaryFileStorage storage) =>
        new(new(new InstanceSummary("oss", "oss", "test", "cloud", [], TypeId: "storage", ProviderId: "aliyun-oss")),
            service, dialogs, temporaryFiles: storage);

    /// <summary>费用确认替身只确认图片，其他测试可显式改为取消或延迟。</summary>
    internal static ObjectStorageTestDialogs ConfirmingDialogs() => new()
    {
        ForcePreviewHandler = (_, _, _) => Task.FromResult<ObjectStorage.UI.Models.ObjectStoragePreviewType?>(ObjectStorage.UI.Models.ObjectStoragePreviewType.Image),
    };

    /// <summary>真实编码器生成 2×2 图片，避免测试依赖外部附件及损坏的固定字节。</summary>
    internal static byte[] ImageBytes()
    {
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255 }, 8);
        image.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
