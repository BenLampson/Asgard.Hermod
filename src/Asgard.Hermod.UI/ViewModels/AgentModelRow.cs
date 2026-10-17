namespace Asgard.Hermod.UI.ViewModels;

/// <summary>列表只暴露非敏感展示字段，不把 API Key 绑定到控件。</summary>
public sealed class AgentModelRow
{
    internal AgentModelRow(AgentModelProfile profile, string defaultId, string selectedId = "", long epoch = 0) { Profile = profile; IsDefault = profile.Id == defaultId; IsSelected = profile.Id == selectedId; Epoch = epoch; }
    internal AgentModelProfile Profile { get; }
    internal long Epoch { get; }
    /// <summary>稳定配置标识。</summary>
    public string Id => Profile.Id;
    /// <summary>用户定义的配置名称。</summary>
    public string Name => Profile.Name;
    /// <summary>仅显示模型与服务主机，不显示凭据。</summary>
    public string Detail => Text.Format("AgentModelIdentity", Profile.Model, new Uri(Profile.Endpoint).Authority);
    /// <summary>新会话默认标志。</summary>
    public bool IsDefault { get; }
    /// <summary>当前来源选择，与全局默认值独立。</summary>
    public bool IsSelected { get; }
    /// <summary>默认操作的真实状态。</summary>
    public string DefaultLabel => Text.Get(IsDefault ? "AgentModelCurrentDefault" : "AgentModelMakeDefault");
}
