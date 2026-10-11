using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>验证控件渲染、输入法提交文本和可视树生命周期。</summary>
public sealed class TerminalControlTests
{
    /// <summary>解除挂载后重新加载仍可渲染中文与颜色，并再次向会话传递尺寸。</summary>
    [Fact]
    public void Control_RendersAfterUnloadAndReload() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(200, 60);
        using var control = new TerminalControl { Session = session };
        control.Measure(new Size(640, 320));
        control.Arrange(new Rect(0, 0, 640, 320));
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        session.FeedOutput(Encoding.UTF8.GetBytes("\x1b[32mHermod 中文\x1b[0m"));
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        control.InvalidateVisual();
        control.UpdateLayout();
        var bitmap = new RenderTargetBitmap(640, 320, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(control);
        byte[] pixels = new byte[640 * 320 * 4];
        bitmap.CopyPixels(pixels, 640 * 4, 0);
        Assert.Contains(pixels.Where((_, index) => index % 4 != 3), value => value != 0);
        Assert.Contains("Hermod 中文", session.GetVisibleText());
        Assert.InRange(session.Buffer.Columns, 10, 199);
        Assert.InRange(session.Buffer.Rows, 3, 59);
    });

    /// <summary>中文由 WPF 文本提交事件编码为 UTF-8，而不是仅处理 KeyDown。</summary>
    [Fact]
    public void TextInput_SendsCommittedUnicodeText() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession();
        using var control = new TerminalControl { Session = session };
        string? sent = null;
        session.InputReceived += (_, bytes) => sent = Encoding.UTF8.GetString(bytes.Span);
        var composition = new TextComposition(InputManager.Current, control, "中文输入");
        control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent,
        });
        Assert.Equal("中文输入", sent);
        Assert.Equal(string.Empty, session.GetVisibleText().Trim());
    });

    /// <summary>导出的选区不会重复包含双列字符的后续单元格。</summary>
    [Fact]
    public void Selection_ExportsChineseWithoutContinuationCells() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession();
        using var control = new TerminalControl { Session = session };
        session.FeedOutput(Encoding.UTF8.GetBytes("中文 hello"));
        control.SelectAll();
        Assert.Equal("中文 hello", control.GetSelectedText().Trim());
        control.Dispose();
        Assert.False(session.IsDisposed);
        session.FeedOutput("!"u8);
    });
}
