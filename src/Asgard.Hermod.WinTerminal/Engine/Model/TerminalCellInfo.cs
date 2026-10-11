// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Model;

/// <summary>保存一个终端单元格的 Unicode 字符与渲染样式。</summary>
[DebuggerDisplay("{'{Character.Value}'}")]
public struct TerminalCellInfo(Rune character, CellRenderStyle style) : IEquatable<TerminalCellInfo>
{
    /// <summary>字符。</summary>
    public Rune Character { get; set; } = character;
    /// <summary>文字样式。</summary>
    public CellRenderStyle Style { get; set; } = style;
    /// <summary>空白单元格。</summary>
    public static TerminalCellInfo Blank { get; } = new TerminalCellInfo(new Rune(' '), CellRenderStyle.Default);

    /// <summary>恢复空白内容或默认状态。</summary>
    public void Clear()
    {
        Character = new Rune(' ');
        Style = CellRenderStyle.Default;
    }

    /// <summary>比较两个值是否相等。</summary>
    public readonly bool Equals(TerminalCellInfo other) => Character == other.Character && Style == other.Style;
    /// <summary>比较两个值是否相等。</summary>
    public override readonly bool Equals(object? obj) => obj is TerminalCellInfo other && Equals(other);
    /// <summary>计算值的哈希代码。</summary>
    public override readonly int GetHashCode() => HashCode.Combine(Character, Style);
    /// <summary>比较或组合终端值。</summary>
    public static bool operator ==(TerminalCellInfo left, TerminalCellInfo right) => left.Equals(right);
    /// <summary>比较或组合终端值。</summary>
    public static bool operator !=(TerminalCellInfo left, TerminalCellInfo right) => !left.Equals(right);
}
