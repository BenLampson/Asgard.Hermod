namespace Asgard.Hermod.Abstractions.Models;

/// <summary>默认询问；关闭询问后使用记录动作，首次默认优先托盘。</summary>
public sealed record MainWindowClosePreference(bool AskBeforeClosing = true, MainWindowCloseAction Action = MainWindowCloseAction.HideToTray);
