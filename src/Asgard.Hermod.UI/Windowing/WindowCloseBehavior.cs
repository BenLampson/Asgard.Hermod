namespace Asgard.Hermod.UI.Windowing;

/// <summary>关闭按钮按真实生命周期显示提示，工作窗口始终保留普通关闭语义。</summary>
public static class WindowCloseBehavior
{
    /// <summary>仅主窗体使用偏好驱动的关闭文案，工作窗体仍使用普通关闭。</summary>
    public static readonly DependencyProperty IsMainWindowProperty = DependencyProperty.RegisterAttached("IsMainWindow", typeof(bool), typeof(WindowCloseBehavior), new PropertyMetadata(false));
    /// <summary>询问状态、默认动作与托盘可用性共同决定文案。</summary>
    public static readonly DependencyProperty CloseActionLabelProperty = DependencyProperty.RegisterAttached("CloseActionLabel", typeof(string), typeof(WindowCloseBehavior), new PropertyMetadata(""));
    /// <summary>读取主窗体标记。</summary>
    public static bool GetIsMainWindow(DependencyObject target) => (bool)target.GetValue(IsMainWindowProperty);
    /// <summary>应用主窗体偏好文案。</summary>
    public static void SetIsMainWindow(DependencyObject target, bool value) => target.SetValue(IsMainWindowProperty, value);
    /// <summary>读取关闭文案。</summary>
    public static string GetCloseActionLabel(DependencyObject target) => (string)target.GetValue(CloseActionLabelProperty);
    /// <summary>偏好或托盘状态变化后即时更新。</summary>
    public static void SetCloseActionLabel(DependencyObject target, string value) => target.SetValue(CloseActionLabelProperty, value);
    /// <summary>仅在主窗体且通知区域可用时设置。</summary>
    public static readonly DependencyProperty HideToTrayProperty = DependencyProperty.RegisterAttached(
        "HideToTray", typeof(bool), typeof(WindowCloseBehavior), new PropertyMetadata(false));
    /// <summary>读取关闭动作的稳定状态。</summary>
    public static bool GetHideToTray(DependencyObject target) => (bool)target.GetValue(HideToTrayProperty);
    /// <summary>托盘注册状态变化后立即更新 tooltip 和无障碍名称。</summary>
    public static void SetHideToTray(DependencyObject target, bool value) => target.SetValue(HideToTrayProperty, value);
}
