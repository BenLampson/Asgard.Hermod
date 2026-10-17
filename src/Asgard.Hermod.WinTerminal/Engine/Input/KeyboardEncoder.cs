// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Input;

/// <summary>将特殊按键、修饰键与粘贴转换成 xterm 输入序列。</summary>
public static class KeyboardEncoder
{
    /// <summary>按当前终端模式编码按键。</summary>
    public static string? Encode(TerminalKey key, TerminalModifier modifiers, in TerminalModes modes)
    {
        int mod = EncodeModifiers(modifiers);
        bool hasModifier = mod > 1;
        return key switch
        {
            TerminalKey.Enter => modes.LineFeedNewLine ? "\r\n" : "\r",
            TerminalKey.Back => modes.BackspaceSendsControlH ? "\b" : "\x7f",
            TerminalKey.Tab => hasModifier && (modifiers & TerminalModifier.Shift) != 0 ? "\x1b[Z" : "\t",
            TerminalKey.Escape => "\x1b",
            TerminalKey.Up => Arrow('A', mod, hasModifier, modes.ApplicationCursorKeys),
            TerminalKey.Down => Arrow('B', mod, hasModifier, modes.ApplicationCursorKeys),
            TerminalKey.Right => Arrow('C', mod, hasModifier, modes.ApplicationCursorKeys),
            TerminalKey.Left => Arrow('D', mod, hasModifier, modes.ApplicationCursorKeys),
            TerminalKey.Home => TildeKey(1, mod, hasModifier),
            TerminalKey.Insert => TildeKey(2, mod, hasModifier),
            TerminalKey.Delete => TildeKey(3, mod, hasModifier),
            TerminalKey.End => TildeKey(4, mod, hasModifier),
            TerminalKey.PageUp => TildeKey(5, mod, hasModifier),
            TerminalKey.PageDown => TildeKey(6, mod, hasModifier),
            TerminalKey.F1 => FunctionSs3('P', mod, hasModifier),
            TerminalKey.F2 => FunctionSs3('Q', mod, hasModifier),
            TerminalKey.F3 => FunctionSs3('R', mod, hasModifier),
            TerminalKey.F4 => FunctionSs3('S', mod, hasModifier),
            TerminalKey.F5 => TildeKey(15, mod, hasModifier),
            TerminalKey.F6 => TildeKey(17, mod, hasModifier),
            TerminalKey.F7 => TildeKey(18, mod, hasModifier),
            TerminalKey.F8 => TildeKey(19, mod, hasModifier),
            TerminalKey.F9 => TildeKey(20, mod, hasModifier),
            TerminalKey.F10 => TildeKey(21, mod, hasModifier),
            TerminalKey.F11 => TildeKey(23, mod, hasModifier),
            TerminalKey.F12 => TildeKey(24, mod, hasModifier),
            _ => null,
        };
    }

    /// <summary>按需添加括号粘贴序列。</summary>
    public static string WrapBracketedPaste(string text, bool bracketedPaste) => bracketedPaste ? $"\x1b[200~{text}\x1b[201~" : text;
    /// <summary>处理 Arrow 对应的终端协议或渲染逻辑。</summary>
    private static string Arrow(char letter, int mod, bool hasModifier, bool applicationCursor)
    {
        if (hasModifier)
            return $"\x1b[1;{mod}{letter}";
        return applicationCursor ? $"\x1bO{letter}" : $"\x1b[{letter}";
    }

    /// <summary>处理 TildeKey 对应的终端协议或渲染逻辑。</summary>
    private static string TildeKey(int code, int mod, bool hasModifier) => hasModifier ? $"\x1b[{code};{mod}~" : $"\x1b[{code}~";
    /// <summary>处理 FunctionSs3 对应的终端协议或渲染逻辑。</summary>
    private static string FunctionSs3(char letter, int mod, bool hasModifier) => hasModifier ? $"\x1b[1;{mod}{letter}" : $"\x1bO{letter}";
    /// <summary>处理 EncodeModifiers 对应的终端协议或渲染逻辑。</summary>
    private static int EncodeModifiers(TerminalModifier modifiers)
    {
        int mod = 1;
        if ((modifiers & TerminalModifier.Shift) != 0)
            mod += 1;
        if ((modifiers & TerminalModifier.Alt) != 0)
            mod += 2;
        if ((modifiers & TerminalModifier.Control) != 0)
            mod += 4;
        return mod;
    }
}
