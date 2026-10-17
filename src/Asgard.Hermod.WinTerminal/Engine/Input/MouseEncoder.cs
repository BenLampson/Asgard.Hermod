// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Input;

/// <summary>将按钮、移动与滚轮转换成 xterm 鼠标报告。</summary>
public static class MouseEncoder
{
    /// <summary>编码鼠标按钮报告。</summary>
    public static string EncodeButton(TerminalMouseButton button, bool pressed, int x, int y, TerminalModifier modifiers, bool sgrEncoding)
    {
        int code = !pressed && !sgrEncoding ? 3 : ButtonCode(button, pressed);
        code |= ModifierFlags(modifiers);
        if (sgrEncoding)
        {
            char suffix = pressed ? 'M' : 'm';
            return $"\x1b[<{code};{x + 1};{y + 1}{suffix}";
        }

        return LegacyEncode(code, x, y);
    }

    /// <summary>编码鼠标移动报告。</summary>
    public static string EncodeMotion(TerminalMouseButton? button, int x, int y, TerminalModifier modifiers, bool sgrEncoding)
    {
        int code = ((button is { } b ? ButtonCode(b, pressed: true) : 3) | 32) | ModifierFlags(modifiers);
        return sgrEncoding ? $"\x1b[<{code};{x + 1};{y + 1}M" : LegacyEncode(code, x, y);
    }

    /// <summary>编码滚轮报告。</summary>
    public static string EncodeWheel(bool up, int x, int y, TerminalModifier modifiers, bool sgrEncoding)
    {
        int code = (up ? 64 : 65) | ModifierFlags(modifiers);
        return sgrEncoding ? $"\x1b[<{code};{x + 1};{y + 1}M" : LegacyEncode(code, x, y);
    }

    /// <summary>处理 LegacyEncode 对应的终端协议或渲染逻辑。</summary>
    private static string LegacyEncode(int code, int x, int y)
    {
        int cb = code + 32;
        int cx = ClampCoord(x + 1);
        int cy = ClampCoord(y + 1);
        return $"\x1b[M{(char)cb}{(char)cx}{(char)cy}";
    }

    /// <summary>处理 ClampCoord 对应的终端协议或渲染逻辑。</summary>
    private static int ClampCoord(int v) => Math.Clamp(v, 1, 223) + 32;
    /// <summary>处理 ButtonCode 对应的终端协议或渲染逻辑。</summary>
    private static int ButtonCode(TerminalMouseButton button, bool pressed) => button switch
    {
        TerminalMouseButton.Left => 0,
        TerminalMouseButton.Middle => 1,
        TerminalMouseButton.Right => 2,
        TerminalMouseButton.XButton1 => 8,
        TerminalMouseButton.XButton2 => 9,
        _ => pressed ? 0 : 3,
    };
    /// <summary>处理 ModifierFlags 对应的终端协议或渲染逻辑。</summary>
    private static int ModifierFlags(TerminalModifier modifiers)
    {
        int flags = 0;
        if ((modifiers & TerminalModifier.Shift) != 0)
            flags |= 4;
        if ((modifiers & TerminalModifier.Alt) != 0)
            flags |= 8;
        if ((modifiers & TerminalModifier.Control) != 0)
            flags |= 16;
        return flags;
    }
}
