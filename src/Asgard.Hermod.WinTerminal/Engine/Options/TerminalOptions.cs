// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Options;

/// <summary>配置终端调色板、默认颜色、历史行和字体回退。</summary>
public sealed class TerminalOptions
{
    /// <summary>调色板。</summary>
    public TerminalPalette Palette { get; set; } = new();
    /// <summary>默认前景色。</summary>
    public TerminalColor DefaultForeground { get; set; } = TerminalColor.White;
    /// <summary>默认背景色。</summary>
    public TerminalColor DefaultBackground { get; set; } = TerminalColor.Black;
    /// <summary>历史行数量上限。</summary>
    public int ScrollbackMaxLines { get; set; } = 10000;
    /// <summary>行高倍率。</summary>
    public double LineHeight { get; set; } = 1.0;
    /// <summary>宿主明确指定的 DIP 单元格高度；空值继续使用原字体行高倍率。</summary>
    public double? CellHeight { get; set; }
    /// <summary>字体回退列表。</summary>
    public string[]? FontFallback { get; set; }
    /// <summary>粗体使用亮色开关。</summary>
    public bool BoldAsBright { get; set; } = true;
    /// <summary>OSC 颜色查询开关。</summary>
    public bool AllowOSCColorQueries { get; set; }
    /// <summary>OSC 剪贴板事件开关。</summary>
    public bool AllowClipboard { get; set; } = true;

    /// <summary>复制当前配置或缓冲行。</summary>
    public TerminalOptions Clone() => new()
    {
        Palette = Palette,
        DefaultForeground = DefaultForeground,
        DefaultBackground = DefaultBackground,
        ScrollbackMaxLines = ScrollbackMaxLines,
        LineHeight = LineHeight,
        CellHeight = CellHeight,
        FontFallback = FontFallback,
        BoldAsBright = BoldAsBright,
        AllowOSCColorQueries = AllowOSCColorQueries,
        AllowClipboard = AllowClipboard,
    };
}
