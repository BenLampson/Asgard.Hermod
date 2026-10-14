namespace Asgard.Hermod.WinTerminal;

/// <summary>上下文菜单与快捷键复用同一文字剪贴板和粘贴确认链路。</summary>
public sealed partial class TerminalControl
{
    private void InitializeClipboardCommands()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopySelection(),
            (_, args) => args.CanExecute = !string.IsNullOrWhiteSpace(GetSelectedText())));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, _) => Paste(),
            (_, args) => args.CanExecute = CanPasteText()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll, (_, _) => SelectAll(),
            (_, args) => args.CanExecute = _session is { IsDisposed: false }));
    }
    private bool CanPasteText()
    {
        if (IsReadOnly || _session is not { IsDisposed: false }) return false;
        try { return Clipboard.ContainsText(); }
        catch (ExternalException) { return false; }
    }
}
