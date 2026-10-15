using System.Diagnostics;
using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>离屏验证容器操作集中在底栏，双语主题与窄屏抽屉均保留正确目标。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshMonitorContainerActionsTests(WpfTestFixture fixture)
{
    /// <summary>真实视图只有数据列；按钮随选中项启用，抽屉打开后仍能通过底栏切换内容。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task BottomActionsRenderAndTargetSelectionAcrossWidths(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture; var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var errors = new BindingErrors(); PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        await using var model = new SshMonitorViewModel();
        var connection = new SshMonitorTestData().CreateConnection();
        var view = new SshMonitorDockerView { DataContext = model };
        view.SetResourceReference(Control.ForegroundProperty, "TextBodyBrush");
        var surface = new Border { Background = (Brush)Application.Current.Resources["SurfaceBrush"], Child = view };
        try
        {
            await model.StartAsync(connection); model.IsPaused = true; model.Section = "docker";
            await model.RefreshDetailsCommand.ExecuteAsync();
            await LayoutAsync(surface, 1440);
            var grid = Assert.IsType<DataGrid>(view.FindName("ContainerGrid"));
            var actions = Assert.IsType<Border>(view.FindName("ContainerActionBar"));
            var logs = Assert.IsType<Button>(view.FindName("ViewContainerLogs"));
            var details = Assert.IsType<Button>(view.FindName("ViewContainerDetails"));
            Assert.Equal(5, grid.Columns.Count); Assert.DoesNotContain(grid.Columns, c => c is DataGridTemplateColumn { CanUserSort: false });
            Assert.DoesNotContain(WindowTestVisuals.Descendants<Button>(grid), b => ReferenceEquals(b.Command, model.ReadLogsCommand));
            Assert.False(logs.IsEnabled); Assert.False(details.IsEnabled);
            model.SelectedContainer = model.Containers[1]; await LayoutAsync(surface, 1440);
            Assert.True(logs.IsEnabled); Assert.True(details.IsEnabled);
            Assert.Equal(Text.Get("SshMonitorViewLogs"), Assert.Single(WindowTestVisuals.Descendants<TextBlock>(logs)).Text);
            Assert.Equal(Text.Get("SshMonitorViewDetails"), Assert.Single(WindowTestVisuals.Descendants<TextBlock>(details)).Text);
            AssertBelow(grid, actions, view); AssertInside(logs, actions); AssertInside(details, actions);
            Capture(surface, $"docker-bottom-actions-{language}-{theme}");
            SshMonitorContainerItem? requested = null; model.ContainerLogsRequested += (_, item) => requested = item;
            await model.ReadLogsCommand.ExecuteAsync(logs.CommandParameter); await LayoutAsync(surface, 900);
            Assert.Same(model.SelectedContainer, requested); Assert.False(model.IsContainerPaneOpen);
            Assert.Equal(Visibility.Visible, Assert.IsType<Grid>(view.FindName("ContainerList")).Visibility);
            AssertInside(logs, actions); AssertInside(details, actions);
            Capture(surface, $"docker-open-logs-keeps-list-{language}-{theme}");
            int requests = connection.Commands.Count;
            details.Command.Execute(details.CommandParameter); await LayoutAsync(surface, 520);
            Assert.True(model.IsContainerDetailsOpen);
            Assert.Equal(requests, connection.Commands.Count);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(Assert.IsType<StackPanel>(view.FindName("ContainerDetails"))), b => b.Text == model.SelectedContainer.Id);
            AssertInside(logs, actions); AssertInside(details, actions);
            AssertInside(actions, view);
            Capture(surface, $"docker-bottom-details-narrow-{language}-{theme}");
            model.CloseContainerDetailsCommand.Execute(null); await LayoutAsync(surface, 900);
            Assert.False(model.IsContainerPaneOpen); Assert.Equal(Visibility.Visible, Assert.IsType<Grid>(view.FindName("ContainerList")).Visibility);
            model.Search = "no-match"; await LayoutAsync(surface, 900);
            Assert.Null(model.SelectedContainer); Assert.False(logs.IsEnabled); Assert.False(details.IsEnabled);
            surface.Child = null;
            var monitor = new SshMonitorView { DataContext = model };
            monitor.SetResourceReference(Control.ForegroundProperty, "TextBodyBrush"); surface.Child = monitor;
            await LayoutAsync(surface, 900);
            var empty = Assert.IsType<Border>(monitor.FindName("MonitorEmptyState"));
            var emptyBar = WindowTestVisuals.Named(monitor, "ContainerActionBar");
            AssertBelow(WindowTestVisuals.Named(monitor, "ContainerFilters"), empty, monitor);
            AssertBelow(empty, emptyBar, monitor);
            Capture(surface, $"docker-bottom-empty-{language}-{theme}");
            monitor.DataContext = null;
            Assert.Empty(errors.Messages);
        }
        finally
        {
            view.DataContext = null; PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
            themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
        }
    });

    /// <summary>刷新保留同 ID 目标；筛选、删除与断线不能留下可执行的旧目标。</summary>
    [Fact]
    public Task SelectionRefreshFilterRemovalAndDisconnectKeepCommandsAccurate() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        await using var model = new SshMonitorViewModel(); await model.StartAsync(connection);
        model.IsPaused = true; model.Section = "docker"; await model.RefreshDetailsCommand.ExecuteAsync();
        Assert.False(model.ReadLogsCommand.CanExecute(null)); Assert.False(model.ViewContainerDetailsCommand.CanExecute(null));
        model.SelectedContainer = model.Containers[0];
        model.ViewContainerDetailsCommand.Execute(null); Assert.True(model.IsContainerDetailsOpen);
        string id = model.SelectedContainer.Id;
        await model.RefreshDetailsCommand.ExecuteAsync(); Assert.Equal(id, model.SelectedContainer?.Id);
        Assert.True(model.IsContainerDetailsOpen); Assert.True(model.ReadLogsCommand.CanExecute(null));
        model.ContainerState = "exited";
        Assert.Null(model.SelectedContainer); Assert.False(model.IsContainerPaneOpen); Assert.False(model.ReadLogsCommand.CanExecute(null));
        model.ContainerState = ""; model.SelectedContainer = model.Containers[0];
        connection.Handler = (command, token) => command == SshMonitorDetailReader.ContainerCommand
            ? Task.FromResult(new SshCommandResult("", 0, false)) : data.HandleAsync(command, token);
        await model.RefreshDetailsCommand.ExecuteAsync();
        Assert.Null(model.SelectedContainer); Assert.False(model.ViewContainerDetailsCommand.CanExecute(null));
        connection.Handler = data.HandleAsync; await model.RefreshDetailsCommand.ExecuteAsync(); model.SelectedContainer = model.Containers[0];
        await model.StopAsync(); Assert.False(model.ReadLogsCommand.CanExecute(null)); Assert.False(model.IsContainerPaneOpen);
    });

    private static async Task LayoutAsync(FrameworkElement surface, double width)
    {
        await WpfTestFixture.DrainDispatcherAsync();
        surface.Measure(new Size(width, 560)); surface.Arrange(new Rect(0, 0, width, 560)); surface.UpdateLayout();
        await WpfTestFixture.DrainDispatcherAsync(); surface.UpdateLayout();
    }

    private static void AssertBelow(FrameworkElement grid, FrameworkElement bar, FrameworkElement view) =>
        Assert.True(bar.TranslatePoint(new Point(), view).Y >= grid.TranslatePoint(new Point(0, grid.ActualHeight), view).Y);

    private static void AssertInside(FrameworkElement child, FrameworkElement parent)
    {
        var origin = child.TranslatePoint(new Point(), parent);
        Assert.True(child.ActualWidth > 0 && child.ActualHeight > 0);
        Assert.True(origin.X >= -1 && origin.Y >= -1 && origin.X + child.ActualWidth <= parent.ActualWidth + 1 && origin.Y + child.ActualHeight <= parent.ActualHeight + 1);
    }

    private static void Capture(FrameworkElement surface, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR"); if (directory is null) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }

    private sealed class BindingErrors : TraceListener
    {
        internal List<string> Messages { get; } = [];
        public override void Write(string? message) { if (message?.Contains("Error:", StringComparison.Ordinal) == true) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
