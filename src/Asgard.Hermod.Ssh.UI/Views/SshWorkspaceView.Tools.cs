using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>协议页面只提供当前会话的工具，筛选与扩展列表由公共控件承载。</summary>
public partial class SshWorkspaceView
{
    private RelayCommand? _increaseFont;
    private RelayCommand? _decreaseFont;

    /// <summary>所有工具调用现有本机动作，不向远端注入命令。</summary>
    private void ConfigureTools()
    {
        _increaseFont = new RelayCommand(_ => ResizeFont(1), _ => !_disposed && TerminalView.FontSize < 28);
        _decreaseFont = new RelayCommand(_ => ResizeFont(-1), _ => !_disposed && TerminalView.FontSize > 9);
        SessionTools.Items = new WorkspaceTool[]
        {
            new("search", Text.Get("SshSearchOutput"), "search", new RelayCommand(_ => OpenSearch()),
                Text.Get("WorkspaceTerminalTools"), Text.Get("SshSearchShortcut"), false),
            new("font", Text.Get("SshAdjustFont"), "type", new RelayCommand(_ => OpenFontMenu()),
                Text.Get("WorkspaceDisplayTools"), Text.Format("SshTerminalFontSizeValue", TerminalView.FontSize), false),
            new("clear", Text.Get("SshClearDisplay"), "eraser", new RelayCommand(_ => TerminalView.ClearDisplay()), Text.Get("WorkspaceDisplayTools")),
            new("details", Text.Get("SshConnectionDetails"), "panel-right", _model?.ToggleDetailsCommand ?? new RelayCommand(_ => { }, _ => false), Text.Get("WorkspaceConnectionTools")),
        };
    }

    /// <summary>字体变化限制在可读范围内，并刷新菜单动作的启用条件。</summary>
    private void ResizeFont(int delta)
    {
        TerminalView.FontSize = Math.Clamp(TerminalView.FontSize + delta, 9, 28);
        _increaseFont?.NotifyCanExecuteChanged(); _decreaseFont?.NotifyCanExecuteChanged(); TerminalView.Focus();
        SessionTools.Items = SessionTools.Items?.Select(tool => tool.Id == "font"
            ? tool with { Hint = Text.Format("SshTerminalFontSizeValue", TerminalView.FontSize) } : tool).ToArray();
    }

    /// <summary>字体工具复用公共菜单样式，关闭后恢复终端焦点。</summary>
    private void OpenFontMenu()
    {
        var menu = new ContextMenu { PlacementTarget = SessionTools };
        menu.Items.Add(new MenuItem { Header = Text.Get("SshIncreaseFont"), Command = _increaseFont });
        menu.Items.Add(new MenuItem { Header = Text.Get("SshDecreaseFont"), Command = _decreaseFont });
        menu.Closed += (_, _) => { if (!_disposed && IsLoaded) TerminalView.Focus(); };
        SessionTools.ContextMenu = menu; menu.IsOpen = true;
    }
}
