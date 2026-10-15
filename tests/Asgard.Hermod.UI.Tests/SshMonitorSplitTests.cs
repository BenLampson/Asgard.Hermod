using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证监测标题栏入口、终端空间回收与小窗口的分屏边界。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshMonitorSplitTests(WpfTestFixture fixture)
{
    /// <summary>移除常驻摘要；收起后恢复全部正文高度，不重建连接或终端。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task MonitorUsesHeaderAndReleasesTerminalSpace(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        await using var model = SshWorkspaceTests.Create(new SshTestFactory());
        using var view = new SshWorkspaceView { DataContext = model };
        var window = new HermodWindow { Width = 1440, Height = 1000, Content = view };
        try
        {
            WindowPreviewCapture.Show(window); await model.InitializeAsync();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var action = Assert.IsType<Button>(view.FindName("MonitorAction"));
            Assert.True(action.IsVisible); Assert.True(action.Focusable);
            var terminal = Assert.IsType<TerminalControl>(view.FindName("TerminalView"));
            var panel = Assert.IsType<Border>(view.FindName("MonitorDetails"));
            double fullHeight = terminal.ActualHeight;
            var session = model.Session;
            model.ToggleMonitorCommand.Execute(null);
            await WindowPreviewCapture.VerifyAsync(window, $"ssh-monitor-split-{language}-{theme}");
            Assert.True(panel.IsVisible); Assert.InRange(panel.ActualHeight, 400, 580);
            Assert.True(terminal.ActualHeight >= 80); Assert.True(terminal.ActualHeight < fullHeight - 300);
            var monitorRow = Assert.IsType<RowDefinition>(view.FindName("MonitorRow"));
            monitorRow.Height = new GridLength(360);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            window.Height = 540; window.Width = 900;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(terminal.ActualHeight >= 80);
            model.ToggleMonitorCommand.Execute(null);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.False(panel.IsVisible); Assert.Same(session, model.Session);
            window.Height = 1000;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(fullHeight, terminal.ActualHeight, 1);
            model.IsMonitorOpen = true;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(360, panel.ActualHeight, 1);
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault; }
    });
}
