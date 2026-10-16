namespace Asgard.Hermod.UI.ViewModels;

/// <summary>编辑入口冻结用户点击的配置和账号版本，不从稍后刷新的活动目录重新解释目标。</summary>
public sealed record AgentModelEditRequest(AgentModelProfile? Profile, long Epoch);
