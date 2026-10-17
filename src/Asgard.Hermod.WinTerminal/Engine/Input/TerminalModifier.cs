// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Input;

/// <summary>表示终端输入的修饰键组合。</summary>
[Flags]
public enum TerminalModifier
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
    Meta = 8,
}
