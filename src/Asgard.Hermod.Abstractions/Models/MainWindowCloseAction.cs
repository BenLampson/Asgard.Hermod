namespace Asgard.Hermod.Abstractions.Models;

/// <summary>主窗体关闭后的稳定行为，独立工作窗口不使用此选项。</summary>
public enum MainWindowCloseAction
{
    /// <summary>隐藏主窗体并保留工作窗口。</summary>
    HideToTray,
    /// <summary>进入全应用退出预检和清理。</summary>
    ExitApplication
}
