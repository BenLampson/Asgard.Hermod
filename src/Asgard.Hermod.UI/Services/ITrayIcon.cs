namespace Asgard.Hermod.UI.Services;

/// <summary>主窗体专属通知区域入口，原生注册与 WPF 生命周期隔离。</summary>
public interface ITrayIcon : IDisposable
{
    /// <summary>只有注册成功时允许隐藏主窗体。</summary>
    bool IsAvailable { get; }
    /// <summary>左键、双击或键盘激活。</summary>
    event Action? Activated;
    /// <summary>右键或键盘请求菜单。</summary>
    event Action? MenuRequested;
    /// <summary>Explorer 重建或注册失败后的可用状态变化。</summary>
    event Action? AvailabilityChanged;
    /// <summary>返回真实图标的设备像素矩形，定位不可依赖固定坐标。</summary>
    bool TryGetBounds(out Rect rectangle);
    /// <summary>系统禁用通知时静默跳过。</summary>
    void ShowHiddenHint();
}
