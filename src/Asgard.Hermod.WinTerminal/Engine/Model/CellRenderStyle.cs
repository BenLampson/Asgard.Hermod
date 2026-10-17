// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Model;

/// <summary>表示单元格的颜色、宽度与文字装饰。</summary>
public readonly struct CellRenderStyle : IEquatable<CellRenderStyle>
{
    /// <summary>前景色。</summary>
    public TerminalColor Foreground { get; init; }
    /// <summary>背景色。</summary>
    public TerminalColor Background { get; init; }
    /// <summary>下划线颜色。</summary>
    public TerminalColor UnderlineColor { get; init; }
    /// <summary>样式标记。</summary>
    public CellStyleFlags Flags { get; init; }
    /// <summary>下划线样式。</summary>
    public Underline UnderlineStyle { get; init; }
    /// <summary>超链接编号。</summary>
    public byte HyperlinkId { get; init; }
    /// <summary>默认前景色标记。</summary>
    public bool ForegroundIsDefault => (Flags & CellStyleFlags.ForegroundIsDefault) != 0;
    /// <summary>默认背景色标记。</summary>
    public bool BackgroundIsDefault => (Flags & CellStyleFlags.BackgroundIsDefault) != 0;
    /// <summary>默认下划线颜色标记。</summary>
    public bool UnderlineColorIsDefault => (Flags & CellStyleFlags.UnderlineColorIsDefault) != 0;
    /// <summary>粗体标记。</summary>
    public bool Bold => (Flags & CellStyleFlags.Bold) != 0;
    /// <summary>弱化标记。</summary>
    public bool Faint => (Flags & CellStyleFlags.Faint) != 0;
    /// <summary>斜体标记。</summary>
    public bool Italic => (Flags & CellStyleFlags.Italic) != 0;
    /// <summary>隐藏标记。</summary>
    public bool Conceal => (Flags & CellStyleFlags.Conceal) != 0;
    /// <summary>反色标记。</summary>
    public bool Inverse => (Flags & CellStyleFlags.Inverse) != 0;
    /// <summary>删除线标记。</summary>
    public bool Strikethrough => (Flags & CellStyleFlags.Strikethrough) != 0;
    /// <summary>上划线标记。</summary>
    public bool Overline => (Flags & CellStyleFlags.Overline) != 0;
    /// <summary>哥特体标记。</summary>
    public bool Fraktur => (Flags & CellStyleFlags.Fraktur) != 0;
    /// <summary>边框标记。</summary>
    public bool Framed => (Flags & CellStyleFlags.Framed) != 0;
    /// <summary>环绕标记。</summary>
    public bool Encircled => (Flags & CellStyleFlags.Encircled) != 0;
    /// <summary>双下划线标记。</summary>
    public bool DoublyUnderlined => (Flags & CellStyleFlags.DoublyUnderlined) != 0;
    /// <summary>双列字符标记。</summary>
    public bool Wide => (Flags & CellStyleFlags.Wide) != 0;
    /// <summary>双列字符后续单元格标记。</summary>
    public bool Continuation => (Flags & CellStyleFlags.Continuation) != 0;
    /// <summary>闪烁模式。</summary>
    public Blink Blink => (Flags & CellStyleFlags.BlinkSlow, Flags & CellStyleFlags.BlinkRapid) switch
    {
        (_, CellStyleFlags.BlinkRapid) => Blink.Rapid,
        (CellStyleFlags.BlinkSlow, _) => Blink.Slow,
        _ => Blink.None,
    };
    /// <summary>默认样式。</summary>
    public static CellRenderStyle Default { get; } = new()
    {
        Foreground = TerminalColor.White,
        Background = TerminalColor.Black,
        UnderlineColor = TerminalColor.White,
        Flags = CellStyleFlags.ForegroundIsDefault | CellStyleFlags.BackgroundIsDefault | CellStyleFlags.UnderlineColorIsDefault,
        UnderlineStyle = Underline.None,
        HyperlinkId = 0,
    };

    /// <summary>比较两个值是否相等。</summary>
    public bool Equals(CellRenderStyle other) => Foreground == other.Foreground && Background == other.Background && UnderlineColor == other.UnderlineColor && Flags == other.Flags && UnderlineStyle == other.UnderlineStyle && HyperlinkId == other.HyperlinkId;
    /// <summary>比较两个值是否相等。</summary>
    public override bool Equals(object? obj) => obj is CellRenderStyle other && Equals(other);
    /// <summary>计算值的哈希代码。</summary>
    public override int GetHashCode() => HashCode.Combine(Foreground, Background, UnderlineColor, Flags, UnderlineStyle, HyperlinkId);
    /// <summary>比较或组合终端值。</summary>
    public static bool operator ==(CellRenderStyle left, CellRenderStyle right) => left.Equals(right);
    /// <summary>比较或组合终端值。</summary>
    public static bool operator !=(CellRenderStyle left, CellRenderStyle right) => !left.Equals(right);
}
