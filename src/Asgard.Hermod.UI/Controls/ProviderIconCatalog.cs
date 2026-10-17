using System.Diagnostics.CodeAnalysis;
using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Controls;

/// <summary>官方品牌图标是随程序集嵌入的静态资源，渲染不发起网络请求。</summary>
internal static class ProviderIconCatalog
{
    /// <summary>只允许程序内声明的资源路径，不从实例配置加载任意文件或远端图像。</summary>
    private static readonly IReadOnlyDictionary<string, ImageSource> Icons = new Dictionary<string, ImageSource>(StringComparer.Ordinal)
    {
        ["provider-aliyun-oss"] = Load("aliyun-oss.png"),
        ["provider-tencent-cos"] = Load("tencent-cos.ico"),
        ["provider-volcengine-tos"] = Load("volcengine-tos.png"),
        ["provider-aws-s3"] = Load("aws-s3.png"),
        ["provider-minio"] = Load("minio.png"),
    };

    /// <summary>品牌色保持原样，未知名称交回通用图标目录。</summary>
    internal static bool TryGet(string? key, [NotNullWhen(true)] out ImageSource? image)
    {
        image = null;
        return key is not null && Icons.TryGetValue(key, out image);
    }

    /// <summary>ICO 选取最高分辨率帧，冻结资源以支持跨窗口复用。</summary>
    private static ImageSource Load(string name)
    {
        var uri = new Uri($"pack://application:,,,/Asgard.Hermod.UI;component/Assets/Providers/{name}");
        var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.MaxBy(item => item.PixelWidth)!;
        frame.Freeze();
        return frame;
    }
}
