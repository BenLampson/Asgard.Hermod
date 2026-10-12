namespace Asgard.Hermod.UI.Models;

/// <summary>安全解码后的只读内容，图片已经脱离文件流并冻结。</summary>
public sealed record FilePreviewContent(string Name, string Text, ImageSource? Image)
{
    /// <summary>图片与文本使用互斥的界面区域。</summary>
    public bool IsImage => Image is not null;
    /// <summary>文本不创建浏览器或脚本运行环境。</summary>
    public bool IsText => Image is null;
}
