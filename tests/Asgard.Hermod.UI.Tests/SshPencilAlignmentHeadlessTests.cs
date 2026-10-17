using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实 SSH 视图离屏渲染，与 SSHWindow 的区域边界逐项对照。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshPencilAlignmentHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("zh-Hans", UiTheme.Light, 1600, 1000, 1)]
    [InlineData("en", UiTheme.Dark, 1600, 1000, 1)]
    [InlineData("en", UiTheme.Light, 1600, 1000, 1)]
    [InlineData("zh-Hans", UiTheme.Dark, 1120, 840, 1)]
    [InlineData("en", UiTheme.Light, 3840, 2160, 1)]
    [InlineData("en", UiTheme.Light, 1920, 1080, 2)]
    [InlineData("en", UiTheme.Dark, 3840, 2160, 1)]
    [InlineData("en", UiTheme.Dark, 1920, 1080, 2)]
    [InlineData("zh-Hans", UiTheme.Light, 3840, 2160, 1)]
    [InlineData("zh-Hans", UiTheme.Light, 1920, 1080, 2)]
    [InlineData("zh-Hans", UiTheme.Dark, 3840, 2160, 1)]
    [InlineData("zh-Hans", UiTheme.Dark, 1920, 1080, 2)]
    public Task InstanceToolsAndConsole_MatchReferenceWithoutDesktopWindows(string language, UiTheme theme, double width, double height, double dpi) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(theme);
            var data = new SshMonitorTestData(); var connection = data.CreateConnection(); var factory = new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) };
            var config = new SshTestConfigurationService { Connection = new() { TypeId = SshIds.Type, ProviderId = SshIds.Direct, Parameters = new Dictionary<string, string> { ["host"] = "10.100.11.99", ["user"] = "root", ["password"] = "test-only" } } };
            var dialogs = new SshTestDialogs { Accept = true };
            await using var model = new SshWorkspaceViewModel(new(new("ssh", "生产服务器", "生产服务器", "terminal", [], TypeId: SshIds.Type, ProviderId: SshIds.Direct) { TagIds = ["builtin.environment.production"] }), config, factory, dialogs, dialogs);
            await model.InitializeAsync(); connection.Output("\u001b[38;2;125;211;199mroot@prod-web-01:~# uptime\u001b[0m\r\n 14:32:00 up 18 days, 2:17, 2 users, load average: 0.10, 0.08, 0.06\r\n\r\nroot@prod-web-01:~# ");
            await SshWorkspaceTests.UntilAsync(() => model.Session!.GetVisibleText().Contains("uptime", StringComparison.Ordinal));
            using var bindings = new WpfBindingTrace(); using var view = new SshWorkspaceView { DataContext = model };
            var layout = (WorkspacePageLayout)view.FindName("WorkspaceLayout"); var header = Assert.IsType<WorkspaceHeader>(layout.Toolbar); layout.Toolbar = null; header.DataContext = model; header.ShowWindowControls = true;
            var root = new DockPanel(); root.Resources.MergedDictionaries.Add(Application.Current.Resources); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header); root.Children.Add(view);
            Layout(); await WpfTestFixture.DrainDispatcherAsync(); Layout();
            var rail = (Border)view.FindName("ToolRail"); var consoleBar = (Border)view.FindName("ConsoleToolbar"); var terminal = (Grid)view.FindName("TerminalSurface");
            Assert.Equal(52, rail.ActualWidth); Assert.Equal(new Point(0, 44), rail.TranslatePoint(new(), root)); Assert.Equal(new Point(52, 44), consoleBar.TranslatePoint(new(), root));
            Assert.Equal(38, consoleBar.ActualHeight); Assert.Equal(new Point(52, 82), terminal.TranslatePoint(new(), root)); Assert.Equal(height - 110, terminal.ActualHeight);
            Assert.Equal(160, Assert.IsAssignableFrom<ListBoxItem>(((WorkspaceTabStrip)view.FindName("ConsoleTabs")).ItemContainerGenerator.ContainerFromIndex(0)).ActualWidth);
            Capture(root, $"ssh-console-{language}-{theme}-{width}");
            await model.NewSessionCommand.ExecuteAsync(); Layout(); Assert.Equal(2, model.Consoles.Count); Capture(root, $"ssh-two-consoles-{language}-{theme}-{width}");
            var first = model.Consoles[0]; model.ActiveConsole = first; await model.ToggleToolboxCommand.ExecuteAsync("files"); Layout(); await WpfTestFixture.DrainDispatcherAsync(); Layout();
            Assert.Equal(320, ((ColumnDefinition)view.FindName("ToolboxColumn")).ActualWidth); Assert.Equal(new Point(378, 44), consoleBar.TranslatePoint(new(), root));
            Assert.Equal(new Point(378, 82), terminal.TranslatePoint(new(), root)); Assert.Same(first, model.ActiveConsole); Assert.Equal(".", model.Files.Path); Capture(root, $"ssh-files-{language}-{theme}-{width}");
            ((ColumnDefinition)view.FindName("ToolboxColumn")).Width = new(400); Layout(); model.CollapseToolboxCommand.Execute(null); Layout(); await model.ToggleToolboxCommand.ExecuteAsync("files"); Layout(); Assert.Equal(400, ((ColumnDefinition)view.FindName("ToolboxColumn")).ActualWidth);
            model.CollapseToolboxCommand.Execute(null); Layout(); Assert.Equal(new Point(52, 44), consoleBar.TranslatePoint(new(), root));
            model.ForwardName = "app-web"; await model.AddForwardCommand.ExecuteAsync(); await model.ToggleToolboxCommand.ExecuteAsync("forward"); Layout();
            Assert.Equal(480, ((ColumnDefinition)view.FindName("ToolboxColumn")).ActualWidth); Capture(root, $"ssh-forwards-{language}-{theme}-{width}");
            model.ShowForwardEditorCommand.Execute(null); Layout(); Capture(root, $"ssh-forward-new-{language}-{theme}-{width}"); model.CancelForwardEditorCommand.Execute(null);
            model.Files.Transfers.Add(new("backup.sql", false) { Bytes = 42, StatusKey = "SshTransferCompleted" }); await model.ToggleToolboxCommand.ExecuteAsync("transfers"); Layout();
            Assert.Equal(360, ((ColumnDefinition)view.FindName("ToolboxColumn")).ActualWidth); Capture(root, $"ssh-transfers-{language}-{theme}-{width}"); model.CollapseToolboxCommand.Execute(null); Layout();
            model.IsMonitorOpen = true; Layout(); await WpfTestFixture.DrainDispatcherAsync(); Layout();
            Assert.Equal(height - 608, ((GridSplitter)view.FindName("MonitorSplitter")).TranslatePoint(new(), root).Y);
            Assert.Equal(572, ((Border)view.FindName("MonitorDetails")).ActualHeight); Capture(root, $"ssh-monitor-{language}-{theme}-{width}");
            foreach (string section in new[] { "overview", "processes", "docker", "network", "disks" })
            {
                model.Monitor.Section = section; await model.Monitor.RefreshDetailsCommand.ExecuteAsync(); Layout(); await WpfTestFixture.DrainDispatcherAsync(); Layout();
                Capture(root, $"ssh-monitor-{section}-{language}-{theme}-{width}");
            }
            model.IsMonitorOpen = false; Layout();
            var remaining = model.Consoles[1]; await model.CloseConsoleCommand.ExecuteAsync(first); Layout(); Assert.Same(remaining, model.ActiveConsole);
            foreach (var console in model.Consoles.ToArray()) await model.CloseConsoleCommand.ExecuteAsync(console);
            Layout(); Assert.True(model.IsConnected); Assert.Null(model.Session); Assert.Equal(0, connection.DisposeCount); Capture(root, $"ssh-zero-consoles-{language}-{theme}-{width}");
            var unexpected = bindings.Messages.Where(message => !message.Contains("AncestorType='System.Windows.Window'", StringComparison.Ordinal)).ToArray();
            Assert.True(unexpected.Length == 0, string.Join(Environment.NewLine, unexpected));
            Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            void Layout() { root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); VisualTreeHelper.SetRootDpi(root, new(1, 1)); VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); root.UpdateLayout(); Assert.Equal(dpi, VisualTreeHelper.GetDpi(root).DpiScaleX); }
        }
        finally { Text.SetApplicationCulture(null); }
    });

    private static void Capture(FrameworkElement root, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (directory is null) return;
        Directory.CreateDirectory(directory); var scale = VisualTreeHelper.GetDpi(root); var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * scale.DpiScaleX), (int)(root.ActualHeight * scale.DpiScaleY), scale.PixelsPerInchX, scale.PixelsPerInchY, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, name + $"-{VisualTreeHelper.GetDpi(root).DpiScaleX * 100}pct.png")); encoder.Save(stream);
    }
}
