using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实四千像素物理画布，分别按系统缩放重新布局和绘制，不构造窗口。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class FourKReadingHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light, 3840, 2160, 1)]
    [InlineData("en", UiTheme.Light, 1920, 1080, 2)]
    [InlineData("en", UiTheme.Dark, 3840, 2160, 1)]
    [InlineData("en", UiTheme.Dark, 1920, 1080, 2)]
    [InlineData("zh-Hans", UiTheme.Light, 3840, 2160, 1)]
    [InlineData("zh-Hans", UiTheme.Light, 1920, 1080, 2)]
    [InlineData("zh-Hans", UiTheme.Dark, 3840, 2160, 1)]
    [InlineData("zh-Hans", UiTheme.Dark, 1920, 1080, 2)]
    public Task LogCaptionSearchAndReadingRemainAligned(string language, UiTheme theme, double width, double height, double dpi) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            var connection = new SshWorkspaceTestConnection { Handler = (_, _) => Task.FromResult(new SshCommandResult("2026-10-16 INFO 应用启动 Application started.\n2026-10-16 WARNING Retrying connection.\n2026-10-16 ERROR Connection timed out.\n", 0, false)) };
            using var model = new SshContainerLogViewModel(new(new(new string('c', 64), "orders-api", "orders:1", "running", "healthy", "")), "prod.example:22", connection, null, "Production SSH");
            using var view = new SshContainerLogView { DataContext = model }; var caption = new SshLogCaption { DataContext = model }; var root = new DockPanel();
            root.Resources.MergedDictionaries.Add(Application.Current.Resources); DockPanel.SetDock(caption, Dock.Top); root.Children.Add(caption); root.Children.Add(view);
            using var bindings = new WpfBindingTrace(); await model.RefreshCommand.ExecuteAsync(); await Layout();
            Assert.Equal(48, caption.ActualHeight); Assert.Equal(40, ((Border)view.FindName("LogToolbar")).ActualHeight);
            var editor = (CodeEditor)view.FindName("LogEditor"); Assert.Equal(new Point(0, 88), editor.TranslatePoint(new(), root)); Assert.True(editor.IsReadOnly); Assert.Equal(13, view.LogOutput.FontSize);
            Assert.Equal(280, ((TextBox)view.FindName("SearchInput")).ActualWidth); Assert.Single(connection.Commands);
            ((TextBox)view.FindName("SearchInput")).Text = "ERROR"; await Layout(); Assert.Equal("ERROR", view.LogOutput.SelectedText); Assert.Single(connection.Commands);
            foreach (var button in WindowTestVisuals.Descendants<Button>(root).Where(IsShown))
            {
                var origin = button.TranslatePoint(new(), root); Assert.InRange(origin.X, 0, width - button.ActualWidth + .1); Assert.InRange(origin.Y, 0, height - button.ActualHeight + .1);
            }
            var input = editor.TextInputControl; var expectedColor = ((SolidColorBrush)view.FindResource("LogSelectionBrush")).Color;
            var selected = input.GetRectFromCharacterIndex(input.SelectionStart); AssertTint(input.TranslatePoint(new(selected.Left + 1, selected.Top + .5), root));
            string original = input.Text; input.Select(0, original.IndexOf("2026-10-16 ERROR", StringComparison.Ordinal)); await Layout();
            var secondLine = input.GetRectFromCharacterIndex(original.IndexOf("WARNING", StringComparison.Ordinal));
            AssertTint(new Point(20, input.TranslatePoint(new(0, secondLine.Top + .5), root).Y)); Assert.Equal(original, input.Text);
            ((TextBox)view.FindName("SearchInput")).Text = ""; ((TextBox)view.FindName("SearchInput")).Text = "ERROR"; await Layout();
            void AssertTint(Point point)
            {
                var image = new RenderTargetBitmap(3840, 2160, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32); image.Render(root); var pixel = new byte[4];
                image.CopyPixels(new Int32Rect((int)(point.X * dpi), (int)(point.Y * dpi), 1, 1), pixel, 4, 0);
                Assert.Equal(new[] { expectedColor.B, expectedColor.G, expectedColor.R, expectedColor.A }, pixel);
            }
            Assert.Empty(bindings.Messages); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            if (Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT") is { } directory)
            {
                Directory.CreateDirectory(directory); var image = new RenderTargetBitmap(3840, 2160, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32); image.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(directory, $"log-4k-{language}-{theme}-{dpi * 100}pct.png")); encoder.Save(stream);
            }
            async Task Layout()
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); VisualTreeHelper.SetRootDpi(root, new(1, 1)); VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); await WpfTestFixture.DrainDispatcherAsync();
                }
                Assert.Equal(dpi, VisualTreeHelper.GetDpi(root).DpiScaleX);
            }
        }
        finally { Text.SetApplicationCulture(null); new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light); }
    });
    [Fact]
    public Task WorkspaceCaptionControlsHaveReferenceIconsAndNoOffscreenWindow() => fixture.RunOffscreenAsync(async () =>
    {
        using var trace = new WpfBindingTrace(); var controls = new WindowControls { IsWorkspace = true }; controls.Resources.MergedDictionaries.Add(Application.Current.Resources);
        controls.Measure(new(108, 44)); controls.Arrange(new(0, 0, 108, 44)); controls.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync();
        Assert.Null(controls.Owner); var buttons = WindowTestVisuals.Descendants<Button>(controls).ToArray(); Assert.Equal(3, buttons.Length);
        foreach (var button in buttons) { Assert.True(button.IsEnabled); Assert.Equal(36, button.ActualWidth); Assert.Equal(44, button.ActualHeight); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        Assert.Equal(new[] { 16d, 13d, 17d }, buttons.Select(button => Assert.Single(WindowTestVisuals.Descendants<IconView>(button)).ActualWidth));
        Assert.Empty(Application.Current.Windows.Cast<Window>()); Assert.Empty(trace.Messages);
    });
    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current)) if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return element.ActualWidth > 0 && element.ActualHeight > 0;
    }
}
