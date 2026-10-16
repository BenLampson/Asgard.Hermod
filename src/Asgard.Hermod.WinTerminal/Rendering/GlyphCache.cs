// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Rendering;

/// <summary>缓存主字体与回退字体的字形，并计算终端网格度量。</summary>
public sealed class GlyphCache : IDisposable
{
    /// <summary>默认等宽字体。</summary>
    private static readonly FontFamily _defaultFont = new FontFamily("Consolas");
    /// <summary>回退字体列表。</summary>
    private readonly List<GlyphTypeface> _fallbacks = [];
    /// <summary>字符到字形的缓存。</summary>
    private readonly Dictionary<int, (GlyphTypeface Tf, ushort Glyph)> _cache = [];
    /// <summary>主屏幕或主字体。</summary>
    private GlyphTypeface? _primary;
    /// <summary>当前字体族。</summary>
    private FontFamily _fontFamily = _defaultFont;
    /// <summary>字体大小。</summary>
    private double _emSize;
    /// <summary>字体度量缩放。</summary>
    private double _scale = 1.0;
    /// <summary>网格单元格宽度。</summary>
    private double _cellWidth;
    /// <summary>网格单元格高度。</summary>
    private double _cellHeight;
    /// <summary>文字基线上升高度。</summary>
    private double _ascent;
    /// <summary>下划线偏移。</summary>
    private double _underlinePos;
    /// <summary>下划线粗细。</summary>
    private double _underlineThickness;
    /// <summary>删除线偏移。</summary>
    private double _strikePos;
    /// <summary>删除线粗细。</summary>
    private double _strikeThickness;
    /// <summary>字体配置是否有效。</summary>
    public bool IsValid => _primary is not null;
    /// <summary>单元格宽度。</summary>
    public double CellWidth => _cellWidth;
    /// <summary>单元格高度。</summary>
    public double CellHeight => _cellHeight;
    /// <summary>字体上升高度。</summary>
    public double Ascent => _ascent;
    /// <summary>下划线偏移。</summary>
    public double UnderlinePosition => _underlinePos;
    /// <summary>下划线粗细。</summary>
    public double UnderlineThickness => _underlineThickness;
    /// <summary>删除线偏移。</summary>
    public double StrikethroughPosition => _strikePos;
    /// <summary>删除线粗细。</summary>
    public double StrikethroughThickness => _strikeThickness;
    /// <summary>主字体字形。</summary>
    public GlyphTypeface Primary => _primary ?? throw new InvalidOperationException("GlyphCache not configured.");

    /// <summary>按字体、尺寸和选项重建渲染配置。</summary>
    public void Configure(string family, double emSize, double lineHeight, IReadOnlyList<string>? fallback, double? cellHeight = null)
    {
        _fallbacks.Clear();
        _cache.Clear();
        _emSize = emSize;
        _fontFamily = string.IsNullOrWhiteSpace(family) ? _defaultFont : new FontFamily(family);
        _primary = ResolveTypeface(_fontFamily, FontWeights.Normal, FontStyles.Normal, FontStretches.Normal);
        if (fallback is { Count: > 0 })
        {
            foreach (string? f in fallback)
            {
                if (string.IsNullOrWhiteSpace(f))
                    continue;
                GlyphTypeface tf = ResolveTypeface(new FontFamily(f), FontWeights.Normal, FontStyles.Normal, FontStretches.Normal);
                _fallbacks.Add(tf);
            }
        }

        MeasureMetrics(lineHeight, cellHeight);
    }

    /// <summary>处理 ResolveTypeface 对应的终端协议或渲染逻辑。</summary>
    private static GlyphTypeface ResolveTypeface(FontFamily fontFamily, FontWeight weight, FontStyle style, FontStretch stretch)
    {
        // 复合 FontFamily 不提供单一 GlyphTypeface；逐项解析才能保留打包等宽主字体的度量。
        // pack://application:,,, 内的三个逗号属于 URI，不是字体分隔符。
        foreach (string candidate in System.Text.RegularExpressions.Regex.Split(fontFamily.Source, @"(?<![:,]),(?!,)").Select(value => value.Trim()).Where(value => value.Length != 0))
        {
            const string packRoot = "pack://application:,,,/";
            var resolved = candidate.StartsWith(packRoot, StringComparison.OrdinalIgnoreCase)
                ? new FontFamily(new Uri(packRoot), "./" + candidate[packRoot.Length..]) : new FontFamily(candidate);
            Typeface typeface = new(resolved, style, weight, stretch);
            if (typeface.TryGetGlyphTypeface(out GlyphTypeface? glyphTypeface) && glyphTypeface is not null)
                return glyphTypeface;
        }
        throw new InvalidOperationException($"Unable to resolve glyph typeface for '{fontFamily.Source}'.");
    }

    /// <summary>处理 MeasureMetrics 对应的终端协议或渲染逻辑。</summary>
    private void MeasureMetrics(double lineHeight, double? cellHeight)
    {
        GlyphTypeface tf = Primary;
        _scale = _emSize;
        ushort rawGlyph = GetRawGlyph(tf, 'M');
        double advance = tf.AdvanceWidths[rawGlyph];
        if (advance <= 0)
            advance = 0.6;
        double lineSpacing = _fontFamily.LineSpacing > 0 ? _fontFamily.LineSpacing : 1.0;
        _cellWidth = advance * _scale;
        _underlinePos = tf.UnderlinePosition * _scale;
        _strikePos = tf.StrikethroughPosition * _scale;
        _ascent = Math.Clamp(tf.Baseline * _scale, 0, int.MaxValue);
        _cellHeight = Math.Ceiling(lineSpacing * _scale * Math.Max(0.5, lineHeight));
        if (cellHeight is > 0 && double.IsFinite(cellHeight.Value))
        {
            _cellHeight = Math.Max(_emSize, cellHeight.Value);
            // 与 WPF BlockLineHeight 一样，把显式行高超出的行距放在首行基线之前。
            _ascent += Math.Max(0, _cellHeight - lineSpacing * _scale);
        }
        _underlineThickness = Math.Max(1, tf.UnderlineThickness * _scale);
        _strikeThickness = Math.Max(1, tf.StrikethroughThickness * _scale);
    }

    /// <summary>处理 GetGlyph 对应的终端协议或渲染逻辑。</summary>
    public (GlyphTypeface Tf, ushort Glyph) GetGlyph(Rune rune)
    {
        int cp = rune.Value;
        if (_cache.TryGetValue(cp, out (GlyphTypeface Tf, ushort Glyph) hit))
            return hit;
        GlyphTypeface primary = Primary;
        ushort rawGlyph = GetRawGlyph(primary, cp);
        GlyphTypeface tf = primary;
        if (rawGlyph == 0)
        {
            foreach (GlyphTypeface fb in _fallbacks)
            {
                ushort fg = GetRawGlyph(fb, cp);
                if (fg != 0)
                {
                    rawGlyph = fg;
                    tf = fb;
                    break;
                }
            }
        }

        if (rawGlyph >= tf.GlyphCount)
        {
            tf = primary;
            rawGlyph = 0;
        }

        (GlyphTypeface tf, ushort g) result = (tf, rawGlyph);
        _cache[cp] = result;
        return result;
    }

    /// <summary>处理 GetVariant 对应的终端协议或渲染逻辑。</summary>
    public GlyphTypeface GetVariant(FontWeight weight, FontStyle style) => ResolveTypeface(_fontFamily, weight, style, FontStretches.Normal);
    /// <summary>处理 GetRawGlyph 对应的终端协议或渲染逻辑。</summary>
    private static ushort GetRawGlyph(GlyphTypeface tf, int codepoint)
    {
        if (tf.GlyphCount == 0)
            return 0;
        if (tf.CharacterToGlyphMap.TryGetValue(codepoint, out ushort glyph))
            return glyph;
        return 0;
    }

    /// <summary>释放缓冲区、解析器或字体缓存资源。</summary>
    public void Dispose()
    {
        _primary = null;
        _fallbacks.Clear();
        _cache.Clear();
    }
}
