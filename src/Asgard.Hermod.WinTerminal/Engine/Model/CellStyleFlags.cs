// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Model;

/// <summary>组合单元格的样式标记。</summary>
[Flags]
public enum CellStyleFlags : uint
{
    None = 0,
    Bold = 1 << 0,
    Faint = 1 << 1,
    Italic = 1 << 2,
    Conceal = 1 << 3,
    Inverse = 1 << 4,
    Strikethrough = 1 << 5,
    Overline = 1 << 6,
    Fraktur = 1 << 7,
    Framed = 1 << 8,
    Encircled = 1 << 9,
    ForegroundIsDefault = 1 << 10,
    BackgroundIsDefault = 1 << 11,
    UnderlineColorIsDefault = 1 << 12,
    Wide = 1 << 13,
    Continuation = 1 << 14,
    BlinkSlow = 1 << 15,
    BlinkRapid = 1 << 16,
    DoublyUnderlined = 1 << 17,
}
