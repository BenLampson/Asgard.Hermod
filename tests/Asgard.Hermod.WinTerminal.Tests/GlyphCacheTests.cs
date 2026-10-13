using Asgard.Hermod.WinTerminal.Engine.Rendering;

namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>共享终端必须从等宽主字体计算网格，回退字体只提供缺失字形。</summary>
public sealed class GlyphCacheTests
{
    /// <summary>复合字体不能使用中文回退的全宽拉丁字距，且支持没有空格的列表。</summary>
    [Theory]
    [InlineData("Consolas, Microsoft YaHei UI")]
    [InlineData("Consolas,Microsoft YaHei UI")]
    public void CompositeFontKeepsMonospaceMetrics(string family) => WpfTestThread.Run(() =>
    {
        using var composite = new GlyphCache(); using var primary = new GlyphCache();
        composite.Configure(family, 14, 1, []); primary.Configure("Consolas", 14, 1, []);
        Assert.Equal(primary.CellWidth, composite.CellWidth); Assert.Equal(primary.Primary.FontUri, composite.Primary.FontUri);
        Assert.InRange(composite.CellWidth, 7, 10);
    });
}
