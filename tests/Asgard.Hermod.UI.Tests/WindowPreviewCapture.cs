using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在不激活桌面窗口的情况下验证真实布局，并按需输出视觉核对图片。</summary>
internal static class WindowPreviewCapture
{
    /// <summary>显示透明测试窗口，让真实模板和布局引擎完成测量。</summary>
    internal static void Show(HermodWindow window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Opacity = 0;
        window.Show();
    }

    /// <summary>确认标题与动作栏在指定尺寸内，截图无需显示或激活桌面窗体。</summary>
    internal static async Task VerifyAsync(HermodWindow window, string name)
    {
        await WindowTestVisuals.UpdateLayoutAsync(window);
        var frame = WindowTestVisuals.Frame(window);
        Assert.InRange(frame.ActualWidth, window.Width - 2, window.Width + 2);
        Assert.InRange(frame.ActualHeight, window.Height - 2, window.Height + 2);
        var caption = WindowTestVisuals.Caption(window);
        Assert.Equal(36, caption.ActualHeight);
        if (window.FooterContent is DialogActionBar)
        {
            var footer = WindowTestVisuals.Footer(window);
            Assert.Equal(56, footer.ActualHeight);
            foreach (var button in WindowTestVisuals.Descendants<Button>(footer)
                         .Where(button => button.Visibility == Visibility.Visible))
            {
                var origin = button.TranslatePoint(new Point(), frame);
                Assert.True(origin.Y >= caption.ActualHeight);
                Assert.True(origin.Y + button.ActualHeight <= frame.ActualHeight + 1);
                Assert.True(button.ActualWidth > 0);
            }
        }

        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(frame.ActualWidth),
            (int)Math.Ceiling(frame.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(frame);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}
