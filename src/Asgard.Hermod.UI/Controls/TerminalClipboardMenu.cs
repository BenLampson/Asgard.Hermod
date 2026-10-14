using System.Windows.Automation;

namespace Asgard.Hermod.UI.Controls;

/// <summary>每次打开按当前启动语言更新文案，仍路由到终端已有的剪贴板命令。</summary>
public sealed class TerminalClipboardMenu : ContextMenu
{
    private readonly MenuItem _copy = new() { Command = ApplicationCommands.Copy };
    private readonly MenuItem _paste = new() { Command = ApplicationCommands.Paste };
    private readonly MenuItem _selectAll = new() { Command = ApplicationCommands.SelectAll };

    /// <summary>构造资源时不缓存尚未应用的语言偏好。</summary>
    public TerminalClipboardMenu()
    {
        Items.Add(_copy); Items.Add(_paste); Items.Add(new Separator()); Items.Add(_selectAll);
    }
    /// <summary>仅设置本地化标签和命令目标；不打开窗口、不读写剪贴板。</summary>
    public void PrepareForTarget(WinTerminal.TerminalControl? target)
    {
        Set(_copy, "TerminalCopy"); Set(_paste, "TerminalPaste"); Set(_selectAll, "TerminalSelectAll");
        void Set(MenuItem item, string key)
        {
            string label = Text.Get(key); item.Header = label; AutomationProperties.SetName(item, label);
            item.CommandTarget = target; item.IsEnabled = target is not null;
        }
    }
    /// <summary>菜单首次打开晚于语言偏好应用，复用同一目标的复制与粘贴确认。</summary>
    protected override void OnOpened(RoutedEventArgs args)
    { PrepareForTarget(PlacementTarget as WinTerminal.TerminalControl); base.OnOpened(args); }
    /// <summary>共享样式中的菜单关闭后不保留最后一个终端控件的命令目标。</summary>
    protected override void OnClosed(RoutedEventArgs args)
    { base.OnClosed(args); PrepareForTarget(null); }
}
