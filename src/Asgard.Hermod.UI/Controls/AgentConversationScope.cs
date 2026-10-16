namespace Asgard.Hermod.UI.Controls;

/// <summary>让投影和嵌套证据继承所属模块的模板；移除视图时不查找已经不存在的视觉祖先。</summary>
public static class AgentConversationScope
{
    /// <summary>工具轮已经显示身份行时，后续回复与嵌套证据不再重复该行。</summary>
    public static readonly DependencyProperty HideAssistantIdentityProperty = DependencyProperty.RegisterAttached("HideAssistantIdentity", typeof(bool), typeof(AgentConversationScope), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    /// <summary>读取最近的助手身份展示状态。</summary>
    public static bool GetHideAssistantIdentity(DependencyObject target) => (bool)target.GetValue(HideAssistantIdentityProperty);
    /// <summary>由当前轮的展示投影设置，不影响消息原文或模型身份。</summary>
    public static void SetHideAssistantIdentity(DependencyObject target, bool value) => target.SetValue(HideAssistantIdentityProperty, value);
    /// <summary>所属模块的原始消息模板，不执行任何模板外业务操作。</summary>
    public static readonly DependencyProperty MessageTemplateProperty = DependencyProperty.RegisterAttached("MessageTemplate", typeof(DataTemplate), typeof(AgentConversationScope), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    /// <summary>读取当前继承的原消息模板。</summary>
    public static DataTemplate? GetMessageTemplate(DependencyObject target) => (DataTemplate?)target.GetValue(MessageTemplateProperty);
    /// <summary>由聊天面板同步模块提供的模板。</summary>
    public static void SetMessageTemplate(DependencyObject target, DataTemplate? template) => target.SetValue(MessageTemplateProperty, template);
}
