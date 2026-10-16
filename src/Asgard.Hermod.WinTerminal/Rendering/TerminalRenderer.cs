// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
using Color = Asgard.Hermod.WinTerminal.Engine.Model.TerminalColor;
using WindowsColor = System.Windows.Media.Color;
namespace Asgard.Hermod.WinTerminal.Engine.Rendering;

/// <summary>使用 WPF GlyphRun 绘制终端单元格、背景、选区和文字装饰。</summary>
public sealed class TerminalRenderer : IDisposable
{
    /// <summary>字形缓存。</summary>
    private readonly GlyphCache _glyphs = new();
    /// <summary>按颜色索引的画刷。</summary>
    private readonly Dictionary<uint, Brush> _brushes = [];
    /// <summary>当前字形段的字符。</summary>
    private readonly List<char> _chars = [];
    /// <summary>当前字形段的字形编号。</summary>
    private readonly List<ushort> _glyphIndices = [];
    /// <summary>字形推进宽度。</summary>
    private readonly List<double> _advanceWidths = [];
    /// <summary>字符到字形的映射。</summary>
    private readonly List<ushort> _clusterMap = [];
    /// <summary>终端渲染配置。</summary>
    private TerminalOptions _options = new();
    /// <summary>字体大小。</summary>
    private double _emSize = 14;
    /// <summary>字体配置是否有效。</summary>
    public bool IsValid => _glyphs.IsValid;
    /// <summary>单元格尺寸。</summary>
    public Size CellSize => new Size(_glyphs.CellWidth, _glyphs.CellHeight);
    /// <summary>文字基线。</summary>
    public double Baseline => _glyphs.Ascent;
    /// <summary>单元格宽度。</summary>
    public double CellWidth => _glyphs.CellWidth;
    /// <summary>单元格高度。</summary>
    public double CellHeight => _glyphs.CellHeight;

    /// <summary>按字体、尺寸和选项重建渲染配置。</summary>
    public void Configure(string family, double emSize, TerminalOptions options)
    {
        _options = options;
        _emSize = emSize;
        _glyphs.Configure(family, emSize, options.LineHeight, options.FontFallback, options.CellHeight);
        _brushes.Clear();
    }

    /// <summary>释放缓冲区、解析器或字体缓存资源。</summary>
    public void Dispose() => _glyphs.Dispose();
    /// <summary>绘制一行单元格的背景、字形和装饰。</summary>
    public void RenderRow(DrawingContext drawingContext, Span<TerminalCellInfo> row, TerminalSelection? selection, int rowIndex, float pixelsPerDip)
    {
        double cellW = _glyphs.CellWidth;
        double cellH = _glyphs.CellHeight;
        double baselineY = _glyphs.Ascent;
        int len = row.Length;
        // 整行选区一次填充；按 ANSI 样式分段填充会在非整数像素边界留下抗锯齿细缝。
        if (selection is { } range && rowIndex >= range.StartY && rowIndex <= range.EndY)
        {
            int first = Math.Max(0, rowIndex == range.StartY ? range.StartX : 0);
            int last = Math.Min(len - 1, rowIndex == range.EndY ? range.EndX : len - 1);
            if (last >= first)
                drawingContext.DrawRectangle(Brush(_options.DefaultForeground), null,
                    new Rect(first * cellW, 0, (last - first + 1) * cellW, cellH));
        }
        int col = 0;
        while (col < len)
        {
            CellRenderStyle style = row[col].Style;
            bool selected = selection?.Contains(rowIndex, col) ?? false;
            int runStart = col;
            while (col < len && row[col].Style == style && (selection?.Contains(rowIndex, col) ?? false) == selected)
                col++;
            int runEnd = col;
            double x = runStart * cellW;
            (Color fg, Color bg) = ResolveColors(style, selected);
            if (!selected && (!style.BackgroundIsDefault || style.Inverse))
                drawingContext.DrawRectangle(Brush(bg), null, new Rect(x, 0, (runEnd - runStart) * cellW, cellH));
            if (!style.Continuation)
            {
                Color decoration = selected || style.Conceal || style.UnderlineColorIsDefault ? fg : style.UnderlineColor;
                DrawGlyphRun(drawingContext, row, runStart, runEnd, x, baselineY, Brush(fg), Brush(decoration), style, pixelsPerDip);
            }
        }
    }

    /// <summary>处理 DrawGlyphRun 对应的终端协议或渲染逻辑。</summary>
    private void DrawGlyphRun(DrawingContext drawingContext, Span<TerminalCellInfo> row, int start, int end, double x, double baselineY, Brush fgBrush, Brush decorationBrush, CellRenderStyle style, float pixelsPerDip)
    {
        double cellW = _glyphs.CellWidth;
        GlyphTypeface? current = null;
        _chars.Clear();
        _glyphIndices.Clear();
        _advanceWidths.Clear();
        _clusterMap.Clear();
        Span<char> text = stackalloc char[2];
        int segCellCount = 0;
        double segX = x;
        void Flush()
        {
            if (current is null || _chars.Count == 0)
            {
                segCellCount = 0;
                _chars.Clear();
                _glyphIndices.Clear();
                _advanceWidths.Clear();
                _clusterMap.Clear();
                return;
            }

            double segWidth = segCellCount * cellW;
            GlyphRun run = new(current, bidiLevel: 0, isSideways: false, renderingEmSize: _emSize, pixelsPerDip: pixelsPerDip, glyphIndices: _glyphIndices.ToArray(), baselineOrigin: new Point(segX, baselineY), advanceWidths: _advanceWidths.ToArray(), glyphOffsets: null, characters: _chars.ToArray(), deviceFontName: null, clusterMap: _clusterMap.ToArray(), caretStops: null, language: XmlLanguage.GetLanguage("en-us"));
            drawingContext.DrawGlyphRun(fgBrush, run);
            DrawDecorations(drawingContext, style, segX, segWidth, baselineY, decorationBrush);
            segX += segWidth;
            _chars.Clear();
            _glyphIndices.Clear();
            _advanceWidths.Clear();
            _clusterMap.Clear();
            segCellCount = 0;
        }

        for (int i = start; i < end; i++)
        {
            ref readonly TerminalCellInfo cell = ref row[i];
            if (cell.Style.Continuation)
                continue;
            Rune rune = cell.Character;
            (GlyphTypeface tf, ushort glyph) = _glyphs.GetGlyph(rune);
            if (current is not null && !ReferenceEquals(tf, current))
                Flush();
            current = tf;
            bool encoded = rune.TryEncodeToUtf16(text, out int charsWritten);
            Debug.Assert(encoded && charsWritten > 0);
            ushort glyphIndex = (ushort)_glyphIndices.Count;
            for (int c = 0; c < charsWritten; c++)
            {
                _chars.Add(text[c]);
                _clusterMap.Add(glyphIndex);
            }

            _glyphIndices.Add(glyph);
            _advanceWidths.Add(cellW);
            segCellCount++;
        }

        Flush();
    }

    /// <summary>处理 DrawDecorations 对应的终端协议或渲染逻辑。</summary>
    private void DrawDecorations(DrawingContext drawingContext, CellRenderStyle style, double x, double width, double baselineY, Brush decoBrush)
    {
        double thickness = Math.Max(1, _glyphs.UnderlineThickness);
        Pen pen = new(decoBrush, thickness);
        if (style.UnderlineStyle != Underline.None || style.DoublyUnderlined)
        {
            double y = baselineY - _glyphs.UnderlinePosition;
            drawingContext.DrawLine(pen, new Point(x, y), new Point(x + width, y));
            if (style.UnderlineStyle == Underline.Double)
                drawingContext.DrawLine(pen, new Point(x, y - thickness - 1), new Point(x + width, y - thickness - 1));
        }

        if (style.Strikethrough)
        {
            double y = baselineY - _glyphs.StrikethroughPosition;
            drawingContext.DrawLine(pen, new Point(x, y), new Point(x + width, y));
        }

        if (style.Overline)
            drawingContext.DrawLine(pen, new Point(x, 0), new Point(x + width, 0));
    }

    /// <summary>处理 ResolveColors 对应的终端协议或渲染逻辑。</summary>
    private (Color Fg, Color Bg) ResolveColors(CellRenderStyle style, bool selected)
    {
        Color fg = style.ForegroundIsDefault ? _options.DefaultForeground : style.Foreground;
        Color bg = style.BackgroundIsDefault ? _options.DefaultBackground : style.Background;
        if (style.Inverse)
            (fg, bg) = (bg, fg);
        // 选区覆盖所有 ANSI 配色，避免彩色文件名和反色输出在连续选区中留下背景缺口。
        if (selected)
            (fg, bg) = (_options.DefaultBackground, _options.DefaultForeground);
        // 隐藏属性最后应用，选中也不能让隐藏文字显露。
        if (style.Conceal)
            fg = bg;
        return (fg, bg);
    }

    /// <summary>处理 Brush 对应的终端协议或渲染逻辑。</summary>
    private Brush Brush(Color color)
    {
        uint key = (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);
        if (!_brushes.TryGetValue(key, out Brush? brush))
        {
            brush = new SolidColorBrush(WindowsColor.FromArgb(color.A, color.R, color.G, color.B));
            _brushes[key] = brush;
        }

        return brush;
    }
}
