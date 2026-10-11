// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Enums;

/// <summary>表示终端文字的下划线样式。</summary>
public enum Underline
{
    None,
    Single,
    Double,
    Curly,
    Dotted,
    Dashed,
}
