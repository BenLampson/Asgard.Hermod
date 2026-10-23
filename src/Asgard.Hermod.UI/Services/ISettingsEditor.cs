namespace Asgard.Hermod.UI.Services;

/// <summary>由宿主打开系统设置窗口。</summary>
public interface ISettingsEditor
{
    /// <summary>显示非模态设置窗口；已打开时只恢复并聚焦，保留当前页面。</summary>
    Task OpenAsync();
    /// <summary>从实例列表直接进入现有标签管理。</summary>
    Task OpenTagsAsync() => OpenAsync();
    /// <summary>从独立助手直接进入模型配置。</summary>
    Task OpenAgentAsync() => OpenAsync();
    /// <summary>从更新提示直接进入版本和发布说明。</summary>
    Task OpenUpdatesAsync() => OpenAsync();
}
