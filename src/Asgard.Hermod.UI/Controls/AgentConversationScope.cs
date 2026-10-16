namespace Asgard.Hermod.UI.Controls;

/// <summary>让投影和嵌套证据继承所属模块的模板；移除视图时不查找已经不存在的视觉祖先。</summary>
public static class AgentConversationScope
{
    /// <summary>所属模块的原始消息模板，不执行任何模板外业务操作。</summary>
    public static readonly DependencyProperty MessageTemplateProperty = DependencyProperty.RegisterAttached("MessageTemplate", typeof(DataTemplate), typeof(AgentConversationScope), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    /// <summary>读取当前继承的原消息模板。</summary>
    public static DataTemplate? GetMessageTemplate(DependencyObject target) => (DataTemplate?)target.GetValue(MessageTemplateProperty);
    /// <summary>由聊天面板同步模块提供的模板。</summary>
    public static void SetMessageTemplate(DependencyObject target, DataTemplate? template) => target.SetValue(MessageTemplateProperty, template);
}
