namespace Asgard.Hermod.UI.Controls;

/// <summary>向页面的嵌套控件传递连接反馈，独立挂载页签时允许没有连接上下文。</summary>
public static class WorkspaceConnectionScope
{
    /// <summary>可继承的连接状态；子控件不需要查找尚未挂载的父视图。</summary>
    public static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(WorkspaceConnectionViewModel),
        typeof(WorkspaceConnectionScope), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    /// <summary>读取当前页面或最近父容器提供的连接状态。</summary>
    public static WorkspaceConnectionViewModel? GetState(DependencyObject target) => (WorkspaceConnectionViewModel?)target.GetValue(StateProperty);
    /// <summary>页面绑定自己的状态，独立预览和编辑器可以保留空上下文。</summary>
    public static void SetState(DependencyObject target, WorkspaceConnectionViewModel? state) => target.SetValue(StateProperty, state);
}
