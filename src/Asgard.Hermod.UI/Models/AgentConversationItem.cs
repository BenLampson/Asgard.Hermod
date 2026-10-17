namespace Asgard.Hermod.UI.Models;

/// <summary>展示投影包裹原对象；既不替换业务消息，也不改变归档和单次批准的所有权。</summary>
public sealed class AgentConversationItem(object message, bool isGroup = false) : ObservableObject
{
    /// <summary>业务原消息或纯展示的工具组。</summary>
    public object Message { get; } = message;
    /// <summary>是否使用连续工具摘要模板。</summary>
    public bool IsGroup { get; } = isGroup;
    /// <summary>工具先于最终回复时，把助手身份放在这一轮的首个工具之前。</summary>
    public bool ShowToolTurnHeader { get; internal set => SetProperty(ref field, value); }
    /// <summary>同一工具轮的后续回复不重复显示身份行。</summary>
    public bool HideMessageIdentity { get; internal set => SetProperty(ref field, value); }
    /// <summary>身份来自该轮真实回复，而不是当前模型选择。</summary>
    public string ModelIdentity { get; internal set => SetProperty(ref field, value); } = "";
}
