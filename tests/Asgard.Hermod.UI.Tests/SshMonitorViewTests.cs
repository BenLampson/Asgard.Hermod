using System.Diagnostics;
using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Controls;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在透明窗口中渲染实际五页，检查双语主题、绑定、数值网格与日志抽屉。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshMonitorViewTests(WpfTestFixture fixture)
{
    /// <summary>所有页面使用相同终端和真实 WPF 模板；截图数据只来自测试协议替身。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task FivePagesRenderWithStructuredGridsAndCharts(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture; var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var trace = new BindingErrors(); PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        var factory = new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) };
        await using var model = SshWorkspaceTests.Create(factory);
        using var view = new SshWorkspaceView { DataContext = model };
        var layout = Assert.IsType<WorkspacePageLayout>(view.FindName("WorkspaceLayout"));
        var header = Assert.IsType<WorkspaceHeader>(layout.Toolbar); header.DataContext = model; layout.Toolbar = null; header.ShowWindowControls = true;
        var window = new HermodWindow { Width = 1440, Height = 1000, CaptionHeight = 72, CaptionContent = header, Content = view };
        try
        {
            WindowPreviewCapture.Show(window); await model.InitializeAsync();
            connection.Output("root@prod-web-01:~# uptime\r\n 14:32:00 up 18 days, load average: 0.18, 0.12, 0.09\r\n\r\nroot@prod-web-01:~# systemctl is-active nginx\r\nactive\r\n\r\nroot@prod-web-01:~# ");
            await SshWorkspaceTests.UntilAsync(() => model.Session!.GetVisibleText().Contains("uptime", StringComparison.Ordinal));
            model.IsMonitorOpen = true;
            await model.Monitor.RefreshDetailsCommand.ExecuteAsync();
            model.Monitor.History.Clear();
            var now = DateTimeOffset.UtcNow;
            for (int i = 0; i < 24; i++) model.Monitor.History.Add(new(now.AddSeconds(-120 + i * 5), 5 + Math.Abs(Math.Sin(i * 0.6)) * 18, 32 + i * 0.1,
                (35 + i * 2.6) * 1024, (28 + i * 2.7) * 1024, []));
            var session = model.Session;
            foreach (string section in new[] { "overview", "processes", "docker", "network", "disks" })
            {
                model.Monitor.Section = section; await model.Monitor.RefreshDetailsCommand.ExecuteAsync();
                await WindowPreviewCapture.VerifyAsync(window, $"ssh-monitor-{section}-{language}-{theme}");
                Assert.Null(model.Monitor.RefreshDetailsCommand.LastException);
                Assert.Same(session, model.Session);
                var grids = WindowTestVisuals.Descendants<DataGrid>(view).Where(g => g.IsVisible).ToArray();
                Assert.NotEmpty(grids); Assert.All(grids, g => Assert.True(g.IsReadOnly));
                Assert.All(grids, g => Assert.False(g.AutoGenerateColumns));
                Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBox>(view), box => box.IsVisible && box.IsReadOnly && box.Name != "LogOutput");
                if (section == "overview")
                {
                    var charts = WindowTestVisuals.Descendants<SshMonitorChart>(view).Where(c => c.IsVisible).ToArray();
                    Assert.Equal(3, charts.Length); Assert.All(charts, c => Assert.True(c.ActualWidth > 200));
                    var rootGrid = Assert.Single(grids);
                    Assert.Single(rootGrid.Items);
                    Assert.All(rootGrid.Columns, column => Assert.True(column.ActualWidth >= 30));
                }
            }
            model.Monitor.Section = "docker"; await model.Monitor.RefreshDetailsCommand.ExecuteAsync();
            await model.Monitor.ReadLogsCommand.ExecuteAsync(model.Monitor.Containers[0]);
            await WindowPreviewCapture.VerifyAsync(window, $"ssh-monitor-logs-{language}-{theme}");
            Assert.True(model.Monitor.IsLogOpen);
            window.Width = 900; window.Height = 640;
            await WindowPreviewCapture.VerifyAsync(window, $"ssh-monitor-compact-logs-{language}-{theme}");
            var docker = Assert.Single(WindowTestVisuals.Descendants<SshMonitorDockerView>(view));
            Assert.False(Assert.IsType<Grid>(docker.FindName("ContainerList")).IsVisible);
            model.Monitor.CloseLogsCommand.Execute(null);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(Assert.IsType<Grid>(docker.FindName("ContainerList")).IsVisible);
            Assert.True(Assert.IsType<TerminalControl>(view.FindName("TerminalView")).ActualHeight >= 80);
            model.Monitor.Search = "no-such-container";
            await WindowPreviewCapture.VerifyAsync(window, $"ssh-monitor-empty-{language}-{theme}");
            Assert.True(model.Monitor.ShowEmpty);
            Assert.Empty(trace.Errors);
        }
        finally
        {
            window.Close(); PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
            themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
        }
    });

    private sealed class BindingErrors : TraceListener
    {
        internal List<string> Errors { get; } = [];
        public override void Write(string? message) { if (message?.Contains("Error:", StringComparison.Ordinal) == true) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
