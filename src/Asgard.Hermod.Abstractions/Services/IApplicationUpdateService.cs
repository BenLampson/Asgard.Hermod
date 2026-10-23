namespace Asgard.Hermod.Abstractions.Services;

/// <summary>只查询更新和打开分发页面，不下载、安装或结束应用进程。</summary>
public interface IApplicationUpdateService
{
    /// <summary>当前运行包的版本；未打包时使用程序集版本。</summary>
    Version CurrentVersion { get; }
    /// <summary>当前安装是否使用微软商店查询可用更新。</summary>
    bool UsesStore { get; }
    /// <summary>返回当前分发渠道的可更新状态及独立的公开发布说明。</summary>
    Task<ApplicationUpdateResult> CheckAsync(CancellationToken cancellationToken);
    /// <summary>由用户主动打开固定的官方分发页面。</summary>
    void OpenDownloadPage();
}
