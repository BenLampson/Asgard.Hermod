namespace Asgard.Hermod.UI.ViewModels;

/// <summary>动作标识和显示标签分离，持久化不使用译文。</summary>
public sealed record MainWindowCloseActionOption(MainWindowCloseAction Action, string Label);
