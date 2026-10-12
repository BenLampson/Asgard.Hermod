namespace Asgard.Hermod.UI.Services;

/// <summary>文件打开与只读预览的 Windows 交互边界。</summary>
public interface IFileOpenService
{
    /// <summary>是否支持内置图片或文本预览。</summary>
    bool CanPreview(string fileName);
    /// <summary>用当前本机规则或系统关联打开已完整下载的文件。</summary>
    Task OpenAsync(string localPath, CancellationToken cancellationToken = default);
    /// <summary>显示只读预览，窗口关闭后结束任务。</summary>
    Task PreviewAsync(string localPath, CancellationToken cancellationToken = default);
}
