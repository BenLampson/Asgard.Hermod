namespace Asgard.Hermod.UI.ViewModels;

/// <summary>保留设置的分类结构，只开放已有设计和实现的页面。</summary>
public sealed record SettingsCategoryViewModel(string Id, string Name, string Icon, bool IsAvailable);
