namespace Asgard.Hermod.UI.Windowing;

/// <summary>关闭按钮按真实生命周期显示提示，工作窗口始终保留普通关闭语义。</summary>
public static class WindowCloseBehavior
{
    /// <summary>仅在主窗体且通知区域可用时设置。</summary>
    public static readonly DependencyProperty HideToTrayProperty = DependencyProperty.RegisterAttached(
        "HideToTray", typeof(bool), typeof(WindowCloseBehavior), new PropertyMetadata(false));
    /// <summary>读取关闭动作的稳定状态。</summary>
    public static bool GetHideToTray(DependencyObject target) => (bool)target.GetValue(HideToTrayProperty);
    /// <summary>托盘注册状态变化后立即更新 tooltip 和无障碍名称。</summary>
    public static void SetHideToTray(DependencyObject target, bool value) => target.SetValue(HideToTrayProperty, value);
}
