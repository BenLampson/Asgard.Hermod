using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Tests;

/// <summary>合成同一真实 WPF 窗口与独立 Popup 的原生渲染树，保持实际屏幕锚点。</summary>
internal static class MainMenuScreenshot
{
    internal static void Save(HermodWindow window, ContextMenu menu, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR"); if (directory is null) return;
        var frame = WindowTestVisuals.Frame(window);
        var main = new RenderTargetBitmap((int)frame.ActualWidth, (int)frame.ActualHeight, 96, 96, PixelFormats.Pbgra32); main.Render(frame);
        var popup = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32); popup.Render(menu);
        Point a = menu.PointToScreen(new Point()), b = frame.PointToScreen(new Point()); var dpi = VisualTreeHelper.GetDpi(frame);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(main, new Rect(0, 0, frame.ActualWidth, frame.ActualHeight));
            drawing.DrawImage(popup, new Rect((a.X - b.X) / dpi.DpiScaleX, (a.Y - b.Y) / dpi.DpiScaleY, menu.ActualWidth, menu.ActualHeight));
        }
        var result = new RenderTargetBitmap(main.PixelWidth, main.PixelHeight, 96, 96, PixelFormats.Pbgra32); result.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result));
        using var output = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(output);
    }
}
