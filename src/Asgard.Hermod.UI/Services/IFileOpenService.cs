namespace Asgard.Hermod.UI.Services;

/// <summary>文件打开与只读预览的 Windows 交互边界。</summary>
public interface IFileOpenService
{
    /// <summary>是否支持文本预览或用看图软件打开图片。</summary>
    bool CanPreview(string fileName);
    /// <summary>用当前本机规则或系统关联打开已完整下载的文件。</summary>
    Task OpenAsync(string localPath, CancellationToken cancellationToken = default);
    /// <summary>文本显示只读窗口；图片调用本机打开方式，调用方负责保留外部使用的缓存。</summary>
    Task PreviewAsync(string localPath, CancellationToken cancellationToken = default);
}
