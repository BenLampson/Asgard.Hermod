// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Buffer;

/// <summary>表示 CharAttributes 的终端协议数据。</summary>
public struct CharAttributes
{
    /// <summary>保存或提供 ForegroundSource 对应的终端状态。</summary>
    public ColorSource ForegroundSource;
    /// <summary>保存或提供 ForegroundIndex 对应的终端状态。</summary>
    public int ForegroundIndex;
    /// <summary>保存或提供 ForegroundRgb 对应的终端状态。</summary>
    public TerminalColor ForegroundRgb;
    /// <summary>保存或提供 BackgroundSource 对应的终端状态。</summary>
    public ColorSource BackgroundSource;
    /// <summary>保存或提供 BackgroundIndex 对应的终端状态。</summary>
    public int BackgroundIndex;
    /// <summary>保存或提供 BackgroundRgb 对应的终端状态。</summary>
    public TerminalColor BackgroundRgb;
    /// <summary>保存或提供 UnderlineSource 对应的终端状态。</summary>
    public ColorSource UnderlineSource;
    /// <summary>保存或提供 UnderlineIndex 对应的终端状态。</summary>
    public int UnderlineIndex;
    /// <summary>保存或提供 UnderlineRgb 对应的终端状态。</summary>
    public TerminalColor UnderlineRgb;
    /// <summary>粗体标记。</summary>
    public bool Bold;
    /// <summary>弱化标记。</summary>
    public bool Faint;
    /// <summary>斜体标记。</summary>
    public bool Italic;
    /// <summary>隐藏标记。</summary>
    public bool Conceal;
    /// <summary>反色标记。</summary>
    public bool Inverse;
    /// <summary>删除线标记。</summary>
    public bool Strikethrough;
    /// <summary>上划线标记。</summary>
    public bool Overline;
    /// <summary>哥特体标记。</summary>
    public bool Fraktur;
    /// <summary>边框标记。</summary>
    public bool Framed;
    /// <summary>环绕标记。</summary>
    public bool Encircled;
    /// <summary>双下划线标记。</summary>
    public bool DoublyUnderlined;
    /// <summary>下划线样式。</summary>
    public Underline UnderlineStyle;
    /// <summary>闪烁模式。</summary>
    public Blink Blink;
    /// <summary>超链接编号。</summary>
    public byte HyperlinkId;
    /// <summary>默认样式。</summary>
    public static CharAttributes Default => new()
    {
        ForegroundSource = ColorSource.Default,
        BackgroundSource = ColorSource.Default,
        UnderlineSource = ColorSource.Default,
        UnderlineStyle = Underline.None,
        Blink = Blink.None,
    };

    /// <summary>恢复初始终端状态。</summary>
    public void Reset()
    {
        ForegroundSource = ColorSource.Default;
        BackgroundSource = ColorSource.Default;
        UnderlineSource = ColorSource.Default;
        Bold = Faint = Italic = Conceal = Inverse = Strikethrough = Overline = Fraktur = Framed = Encircled = DoublyUnderlined = false;
        UnderlineStyle = Underline.None;
        Blink = Blink.None;
        HyperlinkId = 0;
    }

    /// <summary>处理 ToCellStyle 对应的终端协议或渲染逻辑。</summary>
    public readonly CellRenderStyle ToCellStyle(TerminalPalette palette, bool boldAsBright)
    {
        CellStyleFlags flags = 0;
        TerminalColor fg, bg, ul;
        if (ForegroundSource == ColorSource.Default)
        {
            flags |= CellStyleFlags.ForegroundIsDefault;
            fg = TerminalColor.White;
        }
        else if (ForegroundSource == ColorSource.Indexed)
        {
            int idx = boldAsBright && Bold && ForegroundIndex < 8 ? ForegroundIndex + 8 : ForegroundIndex;
            fg = palette.GetColor(idx);
        }
        else
        {
            fg = ForegroundRgb;
        }

        if (BackgroundSource == ColorSource.Default)
        {
            flags |= CellStyleFlags.BackgroundIsDefault;
            bg = TerminalColor.Black;
        }
        else if (BackgroundSource == ColorSource.Indexed)
        {
            bg = palette.GetColor(BackgroundIndex);
        }
        else
        {
            bg = BackgroundRgb;
        }

        if (UnderlineSource == ColorSource.Default)
        {
            flags |= CellStyleFlags.UnderlineColorIsDefault;
            ul = TerminalColor.White;
        }
        else if (UnderlineSource == ColorSource.Indexed)
        {
            ul = palette.GetColor(UnderlineIndex);
        }
        else
        {
            ul = UnderlineRgb;
        }

        if (Bold)
            flags |= CellStyleFlags.Bold;
        if (Faint)
            flags |= CellStyleFlags.Faint;
        if (Italic)
            flags |= CellStyleFlags.Italic;
        if (Conceal)
            flags |= CellStyleFlags.Conceal;
        if (Inverse)
            flags |= CellStyleFlags.Inverse;
        if (Strikethrough)
            flags |= CellStyleFlags.Strikethrough;
        if (Overline)
            flags |= CellStyleFlags.Overline;
        if (Fraktur)
            flags |= CellStyleFlags.Fraktur;
        if (Framed)
            flags |= CellStyleFlags.Framed;
        if (Encircled)
            flags |= CellStyleFlags.Encircled;
        if (DoublyUnderlined)
            flags |= CellStyleFlags.DoublyUnderlined;
        if (Blink == Blink.Slow)
            flags |= CellStyleFlags.BlinkSlow;
        else if (Blink == Blink.Rapid)
            flags |= CellStyleFlags.BlinkRapid;
        return new CellRenderStyle
        {
            Foreground = fg,
            Background = bg,
            UnderlineColor = ul,
            Flags = flags,
            UnderlineStyle = UnderlineStyle,
            HyperlinkId = HyperlinkId,
        };
    }
}
