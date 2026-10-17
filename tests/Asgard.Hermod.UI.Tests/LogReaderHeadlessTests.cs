using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>日志状态、来源与实际字形绘制离屏验证，不创建窗口或改变桌面焦点。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class LogReaderHeadlessTests(WpfTestFixture fixture)
{
    private const string Evidence = "2026-10-13T21:42:01Z INFO\tApplication started.\n2026-10-13T21:42:02Z WARNING Retrying the connection.\n2026-10-13T21:42:03Z ERROR Connection timed out.";
    [Fact]
    public Task FirstFailureHasNoInventedSnapshotAndSuccessfulEmptyReadIsDistinct() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection { Handler = (_, _) => Task.FromException<SshCommandResult>(new IOException("unsafe credential and path")) };
        using var model = new SshContainerLogViewModel(new(new(new string('b', 64), "worker", "image", "running", "healthy", "")), "host", connection, null);
        using var view = new SshContainerLogView { DataContext = model };
        await model.RefreshCommand.ExecuteAsync(); await Layout(view, 1080, 712);
        Assert.Null(model.Snapshot); Assert.True(model.CanRetry); Assert.Empty(view.LogOutput.Text);
        Assert.Equal(Text.Get("SshLogNoSnapshot"), model.BodyHeading); Assert.DoesNotContain("unsafe", model.BodyDetail);
        Assert.Equal(Visibility.Visible, ((StackPanel)view.FindName("EmptyBody")).Visibility);
        connection.Handler = (_, _) => Task.FromResult(new SshCommandResult("", 0, false));
        await model.RefreshCommand.ExecuteAsync(); await Layout(view, 1080, 712);
        Assert.NotNull(model.Snapshot); Assert.False(model.HasAlert); Assert.False(model.CanRetry);
        Assert.Equal(Text.Get("SshMonitorLogEmpty"), model.BodyHeading); Assert.Equal("", model.Snapshot.Text);
    });

    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task LoadingFailureSnapshotMetadataAndSourceDetailsRemainTruthful(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            var pending = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var connection = new SshWorkspaceTestConnection { Handler = (_, _) => pending.Task };
            using var model = new SshContainerLogViewModel(new(new(new string('a', 64), "worker", "image", "running", "healthy", "")), "root@prod.example:22", connection, null, "Production SSH");
            using var view = new SshContainerLogView { DataContext = model }; var caption = new SshLogCaption { DataContext = model };
            var root = new DockPanel(); DockPanel.SetDock(caption, Dock.Top); root.Children.Add(caption); root.Children.Add(view);
            using var bindings = new WpfBindingTrace(); await Layout(root, 1080, 760);
            Assert.Empty(connection.Commands); Assert.True(model.IsBodyEmpty); Assert.Null(model.Snapshot);
            var loading = model.RefreshCommand.ExecuteAsync(); await Layout(root, 1080, 760);
            Assert.Same(model.CancelCommand, ((Button)view.FindName("ReadAction")).Command); Assert.Equal(Text.Get("SshMonitorLogLoading"), model.BodyHeading);
            Capture(root, $"log-loading-{language}-{theme}"); model.CancelCommand.Execute(null); await loading;
            connection.Handler = (_, _) => Task.FromResult(new SshCommandResult(Evidence, 0, false));
            await model.RefreshCommand.ExecuteAsync(); await Layout(root, 1080, 760); var snapshot = model.Snapshot;
            string status = model.SnapshotStatus; Assert.False(model.IsBodyEmpty); Assert.False(model.HasAlert); Assert.Contains("500", status);
            Assert.Same(model.RefreshCommand, ((Button)view.FindName("ReadAction")).Command);
            var search = (TextBox)view.FindName("SearchInput"); Assert.Equal(Visibility.Collapsed, ((StackPanel)view.FindName("SearchResults")).Visibility);
            search.Text = "WARNING"; await Layout(root, 1080, 760); Assert.Equal("WARNING", view.LogOutput.SelectedText);
            Assert.Equal(Visibility.Visible, ((StackPanel)view.FindName("SearchResults")).Visibility); Capture(root, $"log-search-{language}-{theme}");
            search.Text = ""; model.TailLines = 1000; connection.Handler = (_, _) => Task.FromResult(new SshCommandResult("unsafe key=value stderr", 1, false));
            await model.RefreshCommand.ExecuteAsync(); await Layout(root, 1080, 760);
            Assert.Same(snapshot, model.Snapshot); Assert.Equal(status, model.SnapshotStatus); Assert.True(model.HasAlert); Assert.True(model.CanRetry);
            Assert.Equal(Text.Get("SshLogRefreshFailedRetained"), model.AlertText); Assert.DoesNotContain("unsafe", model.Status);
            Assert.Equal(Visibility.Visible, ((StackPanel)caption.FindName("ReadAlert")).Visibility); Capture(root, $"log-failed-retained-{language}-{theme}");
            var details = new SshLogSourceDetailsView { DataContext = model }; await Layout(details, 420, 380); Capture(details, $"log-source-{language}-{theme}");
            Assert.Contains(WindowTestVisuals.Descendants<TextBox>(details), box => box.Text == model.Container.Id);
            Assert.Contains(WindowTestVisuals.Descendants<TextBox>(details), box => box.Text == model.Id);
            model.LogFontSize = 16; model.WrapLines = true; await Layout(root, 1080, 760);
            Assert.Equal(16, view.LogOutput.FontSize); Assert.Equal(Text.Format("SshLogEncodingFont", 16), ((TextBlock)view.FindName("EncodingStatus")).Text);
            Assert.Equal(Evidence, model.LogText); Assert.Equal(snapshot, model.Snapshot);
            model.Disconnect(); Assert.False(model.CanRetry); Assert.False(model.RefreshCommand.CanExecute(null)); Assert.True(model.Assistant.PrepareAttachment(false));
            Assert.Empty(bindings.Messages); Assert.Null(PresentationSource.FromVisual(root));
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task ColoredGlyphsAlignWithNativeTextAndPreserveSelection() => fixture.RunOffscreenAsync(async () =>
    {
        new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light);
        var editor = new CodeEditor { IsLogReader = true, Text = Evidence, IsReadOnly = true }; await Layout(editor, 720, 180);
        var layer = (LogTextLayer)editor.FindName("LogPaint"); var rendered = Render(editor);
        var colors = Glyphs(VisualTreeHelper.GetDrawing(layer)).Select(glyph => ((SolidColorBrush)glyph.ForegroundBrush).Color).ToArray();
        string geometry = $"Actual={layer.ActualWidth}/{layer.ActualHeight}; viewport={editor.TextInputControl.ViewportWidth}/{editor.TextInputControl.ViewportHeight}; first/last={editor.TextInputControl.GetFirstVisibleLineIndex()}/{editor.TextInputControl.GetLastVisibleLineIndex()}; " +
            string.Join("; ", Enumerable.Range(0, editor.TextInputControl.LineCount).Select(line => $"{line}: start={editor.TextInputControl.GetCharacterIndexFromLineIndex(line)},length={editor.TextInputControl.GetLineLength(line)},rect={editor.TextInputControl.GetRectFromCharacterIndex(editor.TextInputControl.GetCharacterIndexFromLineIndex(line))}"));
        Assert.Contains(((SolidColorBrush)layer.WarningBrush).Color, colors); Assert.True(colors.Contains(((SolidColorBrush)layer.ErrorBrush).Color), geometry);
        var ink = layer.BodyBrush; layer.WarningBrush = ink; layer.ErrorBrush = ink; await Layout(editor, 720, 180); rendered = Render(editor); Capture(editor, "log-geometry-colored");
        layer.Visibility = Visibility.Collapsed; editor.TextInputControl.Foreground = ink; await Layout(editor, 720, 180); var native = Render(editor); Capture(editor, "log-geometry-native");
        byte[] actual = Pixels(rendered), expected = Pixels(native); int intersection = 0, union = 0;
        for (int y = 4; y < 110; y++) for (int x = 68; x < 700; x++)
        {
            int offset = (y * rendered.PixelWidth + x) * 4;
            bool left = actual[offset] + actual[offset + 1] + actual[offset + 2] < 550;
            bool right = expected[offset] + expected[offset + 1] + expected[offset + 2] < 550;
            if (left || right) union++; if (left && right) intersection++;
        }
        Assert.True(union > 100 && (double)intersection / union > .9, $"Native glyph overlap: {intersection}/{union}");
        var input = editor.TextInputControl; int selected = input.Text.IndexOf("WARNING", StringComparison.Ordinal); input.Select(selected, 7);
        Assert.Equal("WARNING", editor.SelectedText); Assert.Equal(Evidence, editor.Text); Assert.Null(PresentationSource.FromVisual(editor));
        input.Select(0, 0); input.Foreground = Brushes.Transparent; layer.Visibility = Visibility.Visible;
        layer.WarningBrush = ink; layer.ErrorBrush = ink; input.TextWrapping = TextWrapping.Wrap;
        await Layout(editor, 240, 400); Render(editor);
        var gutter = Glyphs(VisualTreeHelper.GetDrawing(layer)).Where(glyph => ((SolidColorBrush)glyph.ForegroundBrush).Color == ((SolidColorBrush)layer.GutterBrush).Color)
            .Select(glyph => new string(glyph.GlyphRun.Characters.ToArray())).ToArray();
        Assert.Equal(new[] { "1", "2", "3" }, gutter); Assert.Equal(Evidence, input.Text);
    });

    private static IEnumerable<GlyphRunDrawing> Glyphs(Drawing? drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph;
        else if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var run in Glyphs(child)) yield return run;
    }
    private static byte[] Pixels(BitmapSource bitmap) { byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels; }
    private static RenderTargetBitmap Render(FrameworkElement root, int scale = 1)
    {
        var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(root); return bitmap;
    }
    private static async Task Layout(FrameworkElement root, double width, double height)
    {
        await WpfTestFixture.DrainDispatcherAsync(); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync();
    }
    private static void Capture(FrameworkElement root, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (directory is null) return; Directory.CreateDirectory(directory);
        foreach (int scale in new[] { 1, 2 })
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Render(root, scale))); using var stream = File.Create(Path.Combine(directory, $"{name}-{scale * 100}pct.png")); encoder.Save(stream);
        }
    }
}
