namespace Asgard.Hermod.UI.Services;

/// <summary>由宿主打开系统设置窗口。</summary>
public interface ISettingsEditor
{
    /// <summary>打开当前用户的设置和标签管理。</summary>
    Task OpenAsync();
    /// <summary>从实例列表直接进入现有标签管理。</summary>
    Task OpenTagsAsync() => OpenAsync();
    /// <summary>从独立助手直接进入模型配置。</summary>
    Task OpenAgentAsync() => OpenAsync();
}
