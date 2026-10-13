using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Tests;

/// <summary>用显式工作区和 DPI 验证启动边界，覆盖开发机之外的分辨率与显示器排列。</summary>
public sealed class WindowStartupPlacementTests
{
    /// <summary>缩放前的期望尺寸保持不变，高缩放或小工作区按可用空间限制尺寸。</summary>
    [Theory]
    [InlineData(0, 0, 1920, 1020, 1, 1, 1360, 860)]
    [InlineData(0, 0, 1920, 1020, 1.25, 1.25, 1360, 768)]
    [InlineData(0, 0, 1920, 1020, 1.5, 1.5, 1232, 632)]
    [InlineData(0, 0, 1366, 728, 1, 1, 1318, 680)]
    [InlineData(0, 0, 1366, 728, 1.5, 1.5, 862.6666667, 437.3333333)]
    [InlineData(0, 0, 3840, 2080, 2, 2, 1360, 860)]
    [InlineData(0, 0, 1080, 1860, 1.5, 1.5, 672, 860)]
    [InlineData(-1920, -1080, 1920, 1020, 1.25, 1.25, 1360, 768)]
    [InlineData(60, 40, 1860, 1040, 1.25, 1.25, 1360, 784)]
    [InlineData(1920, 0, 2560, 1380, 1.25, 1.5, 1360, 860)]
    public void CalculateBounds_FitsAndCentersWithinScaledWorkArea(
        double x, double y, double pixelWidth, double pixelHeight, double scaleX, double scaleY,
        double expectedWidth, double expectedHeight)
    {
        var work = new Rect(x, y, pixelWidth, pixelHeight);
        var result = WindowStartupPlacement.CalculateBounds(work, new DpiScale(scaleX, scaleY), new Size(1360, 860));
        Assert.Equal(expectedWidth, result.Width, 6);
        Assert.Equal(expectedHeight, result.Height, 6);
        Assert.Equal(work.X + work.Width / 2, (result.X + result.Width / 2) * scaleX, 6);
        Assert.Equal(work.Y + work.Height / 2, (result.Y + result.Height / 2) * scaleY, 6);
        Assert.True(result.Left * scaleX >= work.Left);
        Assert.True(result.Top * scaleY >= work.Top);
        Assert.True(result.Right * scaleX <= work.Right);
        Assert.True(result.Bottom * scaleY <= work.Bottom);
    }
}
