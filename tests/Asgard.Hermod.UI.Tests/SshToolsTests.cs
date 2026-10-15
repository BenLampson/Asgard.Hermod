using System.Globalization;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>工具只作用于当前会话，顶部编辑唯一，窄屏不丢主入口。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshToolsTests(WpfTestFixture fixture)
{
    /// <summary>宽窄窗口与双语主题都保留工具和唯一编辑入口，工具操作不发送服务器命令。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task SingleEditEntryAndToolsStayInsideToolbar(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture; var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var factory = new SshTestFactory(); await using var model = SshWorkspaceTests.Create(factory);
        using var view = new SshWorkspaceView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = 1180, Height = 760 };
        try
        {
            WindowPreviewCapture.Show(window); await model.InitializeAsync();
            var tools = Assert.IsType<WorkspaceTools>(view.FindName("SessionTools"));
            var terminal = Assert.IsType<TerminalControl>(view.FindName("TerminalView"));
            Assert.Same(terminal, tools.FocusTarget);
            Assert.Equal(new[] { "search", "font", "clear", "details" }, tools.Items!.Select(item => item.Id));
            foreach (double width in new[] { 1180d, 800d })
            {
                window.Width = width; await WindowPreviewCapture.VerifyAsync(window, $"ssh-tools-toolbar-{language}-{theme}-{width}");
                var edits = WindowTestVisuals.Descendants<Button>(view).Where(button => button.IsVisible && ReferenceEquals(button.Command, model.EditConnectionCommand)).ToArray();
                var edit = Assert.Single(edits); Assert.True(edit.IsVisible);
                Assert.Equal(Text.Get("SshEditConfiguration"), System.Windows.Automation.AutomationProperties.GetName(edit));
                foreach (var entry in new FrameworkElement[] { edit, tools, Assert.IsType<Button>(view.FindName("MoreAction")) })
                {
                    var position = entry.TranslatePoint(new Point(), view);
                    Assert.True(position.X >= 0); Assert.True(position.X + entry.ActualWidth <= view.ActualWidth + 1);
                }
            }
            window.Width = 1180; await WindowTestVisuals.UpdateLayoutAsync(window);
            tools.IsOpen = true; await WindowTestVisuals.UpdateLayoutAsync(window);
            var panel = Assert.IsType<Border>(tools.FindName("ToolsPanel")); Assert.Equal(340, panel.ActualWidth);
            CapturePanel(panel, $"ssh-tools-panel-{language}-{theme}");
            var search = tools.Items!.Single(item => item.Id == "search"); search.Command.Execute(null);
            await WindowTestVisuals.UpdateLayoutAsync(window); Assert.False(tools.IsOpen);
            var input = Assert.IsType<TextBox>(view.FindName("SearchInput")); Assert.True(input.IsVisible);
            tools.Items!.Single(item => item.Id == "details").Command.Execute(null); Assert.True(model.IsDetailsOpen);
            factory.Connections[0].Output("123 numeric output"); await SshWorkspaceTests.UntilAsync(() => model.Session!.GetVisibleText().Contains("numeric", StringComparison.Ordinal));
            tools.Items!.Single(item => item.Id == "clear").Command.Execute(null); Assert.Empty(model.Session!.GetVisibleText().Trim());
            tools.Items!.Single(item => item.Id == "font").Command.Execute(null);
            var menu = Assert.IsType<ContextMenu>(tools.ContextMenu);
            Assert.Equal(2, menu.Items.Count);
            var increase = Assert.IsType<MenuItem>(menu.Items[0]); increase.Command.Execute(null); Assert.Equal(15, terminal.FontSize);
            menu.IsOpen = false; Assert.Contains("15", tools.Items!.Single(item => item.Id == "font").Hint);
            Assert.Empty(factory.Connections[0].Inputs); Assert.Equal(1, factory.Calls);
            var more = Assert.IsType<Button>(view.FindName("MoreAction"));
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.DoesNotContain(more.ContextMenu.Items.OfType<MenuItem>(), item => ReferenceEquals(item.Command, model.EditConnectionCommand));
            more.ContextMenu.IsOpen = false;
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault; }
    });

    /// <summary>导出实际弹出层供视觉核对，输出沿用测试预览目录，不提交图片。</summary>
    private static void CapturePanel(FrameworkElement panel, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth),
            (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(panel); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }
}
