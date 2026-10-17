namespace Asgard.Hermod.UI.Controls;

/// <summary>消息模板直接继承所属控件；视图重排、脱离或嵌套时仍能解析原来的绑定来源。</summary>
public static class AgentMessageScope
{
    /// <summary>每个消息控件覆盖最近的所有者，不依赖跨模板名称查找。</summary>
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.RegisterAttached("Owner", typeof(AgentMessageView), typeof(AgentMessageScope), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    /// <summary>读取当前消息控件。</summary>
    public static AgentMessageView? GetOwner(DependencyObject target) => (AgentMessageView?)target.GetValue(OwnerProperty);
    /// <summary>初始化消息控件自身的模板来源。</summary>
    public static void SetOwner(DependencyObject target, AgentMessageView owner) => target.SetValue(OwnerProperty, owner);
}
