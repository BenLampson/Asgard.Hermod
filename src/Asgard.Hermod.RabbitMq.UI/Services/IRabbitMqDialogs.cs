using Asgard.Hermod.RabbitMq.UI.ViewModels;

namespace Asgard.Hermod.RabbitMq.UI.Services;

/// <summary>视图模型不创建或显示 Windows 窗口，便于离线测试。</summary>
public interface IRabbitMqDialogs
{
    Task<bool> ShowAsync(RabbitMqForm form);
    void Copy(string content);
    Task ExportAsync(byte[] bytes, bool preview);
}
