using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Asgard.Hermod.WinTerminal.Engine.Rendering;

namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>验证跨越 ANSI 彩色输出的选区背景连续、文字可读，取消后恢复原色。</summary>
public sealed class TerminalSelectionRenderingTests
{
    /// <summary>SSH 的小数行高在各 DPI 下跨行选中时，空白背景也不能出现水平细缝。</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void FractionalLineHeightSelectionHasNoRowSeams(double scale) => WpfTestThread.Run(() =>
    {
        var options = CreateOptions(false);
        options.CellHeight = 22.1;
        using var session = new TerminalSession(72, 8, options);
        using var renderer = new TerminalRenderer();
        renderer.Configure("Consolas", 13, options);
        var selected = Draw(renderer, session, new TerminalSelection(0, 0, 71, 7), options, scale);
        int x = selected.PixelWidth / 2;
        for (int y = 1; y < selected.PixelHeight - 1; y++)
            Assert.Equal(ToColor(options.DefaultForeground), Pixel(selected, x, y));
    });

    /// <summary>模拟 ls 彩色文件名与跨行拖选，浅色和深色主题都不能出现未高亮的缺口。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ColoredOutputHasContinuousSelectionAndRestoresOriginalColors(bool light, bool reverse) => WpfTestThread.Run(() =>
    {
        var options = CreateOptions(light);
        using var session = new TerminalSession(72, 3, options);
        const string listing = "\x1b[34mbackups\x1b[0m  release.py  \x1b[32mcleanup.sh\x1b[0m  \x1b[41mtmp\x1b[0m  \x1b[7mstatus\x1b[0m";
        session.FeedOutput(Encoding.UTF8.GetBytes(listing + "\r\nroot@MD-Core:~# ls"));
        using var renderer = new TerminalRenderer();
        renderer.Configure("Consolas", 16, options);
        var before = Draw(renderer, session, null, options);
        var selection = reverse ? new TerminalSelection(5, 1, 2, 0) : new TerminalSelection(2, 0, 5, 1);
        var selected = Draw(renderer, session, selection, options);
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < session.Buffer.Columns; column++)
            {
                int x = (int)((column + 0.5) * renderer.CellWidth);
                int y = (int)(row * renderer.CellSize.Height);
                bool highlighted = row == 0 ? column >= 2 : column <= 5;
                Assert.Equal(highlighted ? ToColor(options.DefaultForeground) : Pixel(before, x, y), Pixel(selected, x, y));
            }
        }
        for (int x = (int)Math.Ceiling(2 * renderer.CellWidth) + 1; x < selected.PixelWidth - 1; x++)
            Assert.Equal(ToColor(options.DefaultForeground), Pixel(selected, x, 0));
        Assert.Equal(Pixels(before), Pixels(Draw(renderer, session, null, options)));
        using var control = new TerminalControl { Session = session };
        control.SelectAll();
        Assert.Contains("backups  release.py  cleanup.sh  tmp  status", control.GetSelectedText());
        ExportPreview(selected, $"terminal-selection-{(light ? "light" : "dark")}-{reverse}.png");
    });

    /// <summary>选中的普通、彩色、反色文字都使用选区前景，隐藏文字仍保持不可见。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedGlyphsUseReadableColorsWithoutRevealingConcealedText(bool light) => WpfTestThread.Run(() =>
    {
        var options = CreateOptions(light);
        using var session = new TerminalSession(20, 3, options);
        session.FeedOutput("\x1b[4;9;53;58;2;255;0;0ma\x1b[34mb\x1b[32mc\x1b[41md\x1b[7me\x1b[0;8mf"u8);
        using var renderer = new TerminalRenderer();
        renderer.Configure("Consolas", 16, options);
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
            renderer.RenderRow(context, session.Buffer.GetRow(0)[..6], new TerminalSelection(0, 0, 5, 0), 0, 1);
        var glyphs = drawing.Children.OfType<GlyphRunDrawing>().ToArray();
        Assert.Equal(6, glyphs.Length);
        for (int index = 0; index < glyphs.Length; index++)
            Assert.Equal(ToColor(index == 5 ? options.DefaultForeground : options.DefaultBackground),
                Assert.IsType<SolidColorBrush>(glyphs[index].ForegroundBrush).Color);
        var decorations = drawing.Children.OfType<GeometryDrawing>().Where(item => item.Pen is not null).ToArray();
        Assert.Equal(15, decorations.Length);
        Assert.All(decorations, item => Assert.Equal(ToColor(options.DefaultBackground),
            Assert.IsType<SolidColorBrush>(item.Pen.Brush).Color));
    });

    /// <summary>固定测试配色，不依赖 Windows 主题或显示语言。</summary>
    private static TerminalOptions CreateOptions(bool light) => new()
    {
        DefaultForeground = TerminalColor.FromArgb(light ? unchecked((int)0xFF20262E) : unchecked((int)0xFFE8EDF4)),
        DefaultBackground = TerminalColor.FromArgb(light ? unchecked((int)0xFFF5F7FA) : unchecked((int)0xFF20262E)),
    };

    /// <summary>通过真实 WPF 绘制与像素栅格化检查背景，不使用渲染器内部配色函数。</summary>
    private static RenderTargetBitmap Draw(TerminalRenderer renderer, TerminalSession session, TerminalSelection? selection, TerminalOptions options, double scale = 1)
    {
        int width = (int)Math.Ceiling(session.Buffer.Columns * renderer.CellWidth);
        int height = (int)(session.Buffer.Rows * renderer.CellSize.Height);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(ToColor(options.DefaultBackground)), null, new Rect(0, 0, width, height));
            for (int row = 0; row < session.Buffer.Rows; row++)
            {
                context.PushTransform(new TranslateTransform(0, row * renderer.CellSize.Height));
                renderer.RenderRow(context, session.Buffer.GetRow(row), selection, row, (float)scale);
                context.Pop();
            }
        }
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    /// <summary>读取不受字形抗锯齿影响的单元格顶部背景像素。</summary>
    private static Color Pixel(BitmapSource bitmap, int x, int y)
    {
        byte[] pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    /// <summary>比较取消选区后的完整画面，确保 ANSI 原色没有被写回缓冲区。</summary>
    private static byte[] Pixels(BitmapSource bitmap)
    {
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    /// <summary>转换终端颜色，不改变测试所指定的透明度。</summary>
    private static Color ToColor(TerminalColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>仅在显式指定本地预览目录时导出验收图片。</summary>
    private static void ExportPreview(BitmapSource bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }
}
