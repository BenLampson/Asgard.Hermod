// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Input;

/// <summary>表示终端特殊按键。</summary>
public enum TerminalKey
{
    None,
    Enter,
    Back,
    Tab,
    Escape,
    Up,
    Down,
    Right,
    Left,
    Home,
    Insert,
    Delete,
    End,
    PageUp,
    PageDown,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
}
