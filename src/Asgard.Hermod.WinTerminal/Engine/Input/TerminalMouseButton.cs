// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Input;

/// <summary>表示终端鼠标按钮。</summary>
public enum TerminalMouseButton
{
    None,
    Left,
    Middle,
    Right,
    XButton1,
    XButton2,
}
