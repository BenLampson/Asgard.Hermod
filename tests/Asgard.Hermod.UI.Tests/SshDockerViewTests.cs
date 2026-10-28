using System.Diagnostics;
using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>实际 XAML 的双语、双主题和宽窄布局验收，不把设计演示数据加入生产代码。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshDockerViewTests(WpfTestFixture fixture)
{
    /// <summary>真实工作区中的页面切换保持可见内容，不被终端或监测空白层遮盖。</summary>
    [Theory]
    [InlineData(UiTheme.Dark)]
    [InlineData(UiTheme.Light)]
    public Task WorkspaceSwitchingKeepsTheManagementPageVisible(UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        await using var model = SshWorkspaceTests.Create(new SshTestFactory { Handler = _ => Task.FromResult<Asgard.Hermod.Ssh.ISshConnection>(connection) });
        using var view = new SshWorkspaceView { DataContext = model };
        try
        {
            await model.InitializeAsync(); model.IsMonitorOpen = true;
            for (int i = 0; i < 4; i++)
            {
                await model.ToggleDockerCommandsCommand.ExecuteAsync();
                await Layout(view, 1388, 920);
                var management = Assert.IsType<SshDockerCommandsView>(view.FindName("DockerCommandsView"));
                Assert.Equal(model.IsDockerCommandsOpen ? Visibility.Visible : Visibility.Collapsed, management.Visibility);
                if (model.IsDockerCommandsOpen)
                {
                    Assert.InRange(management.ActualWidth, 380, 750);
                    var terminal = Assert.IsType<Grid>(view.FindName("TerminalSurface"));
                    Assert.Equal(Visibility.Visible, terminal.Visibility); Assert.True(terminal.ActualWidth > 400);
                    Assert.Same(model.DockerCommands, management.DataContext);
                    if (!model.DockerCommands.IsInitialized) await model.DockerCommands.DetectCommand.ExecuteAsync();
                    model.DockerCommands.SelectedContainer = model.DockerCommands.Containers[1];
                    await model.DockerCommands.ActionCommand.ExecuteAsync("logs");
                    await Layout(view, 1388, 920);
                    Assert.Equal(Visibility.Visible, management.Visibility);
                    Assert.NotEmpty(Assert.IsType<TextBox>(management.FindName("ResultOutput")).Text);
                    Capture(view, $"docker-workspace-{theme}");
                }
            }
        }
        finally { themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>资源优先、直接动作、可选命令详情，双主题和窄屏保持稳定。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task CommandLibraryRendersAcrossLanguagesThemesAndWidths(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        var previous = Text.Culture; Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var errors = new Errors(); PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        var connection = new SshMonitorTestData().CreateConnection();
        using var model = new SshDockerCommandsViewModel(() => connection, () => "ops@md-core.example.com:22", new Dialogs(), () => true, (_, _) => Task.CompletedTask);
        var view = new SshDockerCommandsView { DataContext = model };
        try
        {
            await model.InitializeAsync();
            Assert.Null(model.SelectedContainer);
            Assert.False(Assert.IsType<Expander>(view.FindName("CommandDetails")).IsExpanded);
            model.Select("logs", model.Containers[1]); await model.ActionCommand.ExecuteAsync("logs");
            foreach (double width in new[] { 1388d, 1000d, 640d })
            {
                await Layout(view, width, 820);
                var preview = Assert.IsType<TextBox>(view.FindName("CommandPreview")); var execute = Assert.IsType<Button>(view.FindName("LogsAction"));
                Assert.True(preview.IsReadOnly); Assert.True(execute.IsEnabled); Assert.Equal(model.Preview, preview.Text);
                var list = Assert.IsType<DataGrid>(view.FindName("ContainerList"));
                Assert.True(list.Focusable);
                Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(view.FindName("EmptySelection")).Visibility);
                var item = Assert.IsType<DataGridRow>(list.ItemContainerGenerator.ContainerFromItem(model.SelectedContainer!));
                var bounds = item.RenderSize; var catalogSize = list.RenderSize;
                list.SelectedItem = model.Containers[0]; await Layout(view, width, 820);
                Assert.Equal(bounds, item.RenderSize); Assert.Equal(catalogSize, list.RenderSize);
                list.SelectedItem = model.Containers[1]; await Layout(view, width, 820);
                var search = Assert.IsType<TextBox>(view.FindName("OutputSearch")); search.Text = "health";
                Assert.Equal("health", Assert.IsType<TextBox>(view.FindName("ResultOutput")).SelectedText); search.Text = "";
                Inside(execute, view); Inside(Assert.IsType<Border>(view.FindName("OutputPanel")), view);
                Capture(view, $"docker-commands-{language}-{theme}-{width}");
            }
            model.Select("compose-up", null); model.ProjectDirectory = "/srv/hermod";
            Assert.IsType<Expander>(view.FindName("OtherResources")).IsExpanded = true;
            await Layout(view, 1388, 820);
            Assert.Contains("--no-build", model.Preview, StringComparison.Ordinal); Capture(view, $"docker-compose-{language}-{theme}");
            Assert.Empty(errors.Messages);
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); Text.Initialize(previous.Name, CultureInfo.InvariantCulture); themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>资源与详情位于主区域；结果可关闭、重开并保留手动调整的高度。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task MainPagesAndCollapsibleOutputRemainUsable(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        var previous = Text.Culture; Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var errors = new Errors(); PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        var connection = new SshMonitorTestData().CreateConnection();
        using var model = new SshDockerCommandsViewModel(() => connection, () => "ops@host:22", new Dialogs(), () => true, (_, _) => Task.CompletedTask);
        var view = new SshDockerCommandsView { DataContext = model };
        try
        {
            await model.InitializeAsync(); model.SelectedContainer = model.Containers[1];
            var output = Assert.IsType<Grid>(view.FindName("OutputArea")); var grid = Assert.IsType<Grid>(view.FindName("CommandColumns"));
            foreach (double width in new[] { 380d, 640d, 1000d })
            {
                model.SelectToolCommand.Execute("containers"); await Layout(view, width, 820);
                Assert.Equal(Visibility.Collapsed, output.Visibility); Assert.Equal(0, grid.RowDefinitions[2].ActualHeight);
                var tabs = Assert.IsType<ScrollViewer>(view.FindName("ToolTabsScroll"));
                Assert.True(tabs.ViewportHeight >= Assert.IsType<RadioButton>(view.FindName("ResourcesTab")).ActualHeight);
                double fullHeight = Assert.IsType<DataGrid>(view.FindName("ContainerList")).ActualHeight;
                model.ShowResourcesCommand.Execute(null); await Layout(view, width, 820);
                Assert.Equal(Visibility.Visible, Assert.IsType<Grid>(view.FindName("ResourcePage")).Visibility);
                Assert.Equal(Visibility.Collapsed, output.Visibility);
                Capture(view, $"docker-primary-resources-{language}-{theme}-{width}");
                model.ShowDetailsCommand.Execute(null); await Layout(view, width, 820);
                Assert.Equal(Visibility.Visible, Assert.IsType<Border>(view.FindName("DetailsPage")).Visibility);
                model.SelectToolCommand.Execute("images"); await Layout(view, width, 820);
                var images = Assert.IsType<DataGrid>(view.FindName("ImageList"));
                Assert.Equal(3, images.Items.Count); Assert.True(images.ActualHeight > 0);
                if (width == 380)
                {
                    var scroll = Assert.Single(WindowTestVisuals.Descendants<ScrollViewer>(images));
                    Assert.True(scroll.ScrollableWidth > 0, $"Grid: {images.ActualWidth}, extent: {scroll.ExtentWidth}, viewport: {scroll.ViewportWidth}, visibility: {scroll.HorizontalScrollBarVisibility}, columns: {string.Join(", ", images.Columns.Select(column => $"{column.Width}/{column.ActualWidth}"))}");
                    scroll.ScrollToRightEnd(); await WpfTestFixture.DrainDispatcherAsync();
                    Assert.True(scroll.HorizontalOffset > 0); scroll.ScrollToLeftEnd(); await WpfTestFixture.DrainDispatcherAsync();
                }
                Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(view.FindName("NoImages")).Visibility);
                Capture(view, $"docker-primary-images-{language}-{theme}-{width}");
                model.SelectToolCommand.Execute("containers"); await model.ActionCommand.ExecuteAsync("logs"); await Layout(view, width, 820);
                Assert.Equal(Visibility.Visible, output.Visibility);
                Assert.True(Assert.IsType<DataGrid>(view.FindName("ContainerList")).ActualHeight < fullHeight);
                grid.RowDefinitions[0].Height = new GridLength(300); grid.RowDefinitions[2].Height = new GridLength(390);
                model.ToggleOutputCommand.Execute(null); await Layout(view, width, 820);
                Assert.Equal(Visibility.Collapsed, output.Visibility); Assert.Equal(0, grid.RowDefinitions[2].ActualHeight);
                Assert.InRange(Math.Abs(Assert.IsType<DataGrid>(view.FindName("ContainerList")).ActualHeight - fullHeight), 0, 1);
                model.ToggleOutputCommand.Execute(null); await Layout(view, width, 820);
                Assert.Equal(new GridLength(390), grid.RowDefinitions[2].Height);
                model.ToggleOutputCommand.Execute(null); await Layout(view, width, 820);
                Assert.True(Assert.IsType<Button>(view.FindName("ToggleOutput")).Focusable);
                Capture(view, $"docker-primary-{language}-{theme}-{width}");
            }
            Assert.Empty(errors.Messages);
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); Text.Initialize(previous.Name, CultureInfo.InvariantCulture); themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>资源字段、真实曲线与选择一致，日志、详情和命令入口均保留。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task ResourceMonitorUsesStructuredStatsAndRealCharts(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        var previous = Text.Culture; Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var errors = new Errors(); PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        await using var model = new SshMonitorViewModel(); var connection = new SshMonitorTestData().CreateConnection();
        var view = new SshMonitorDockerView { DataContext = model };
        try
        {
            await model.StartAsync(connection); model.IsPaused = true; model.Section = "docker"; await model.RefreshDetailsCommand.ExecuteAsync();
            model.SelectedContainer = model.Containers.First(c => c.Name == "hermod-api");
            Assert.Equal(28.4, model.SelectedContainer.CpuPercent); Assert.Contains("428", model.ContainerMemory, StringComparison.Ordinal);
            Assert.NotEmpty(model.ContainerCpuPoints); Assert.Null(model.SelectedContainer.ReceivedPerSecond);
            await model.RefreshDetailsCommand.ExecuteAsync(); Assert.Equal(0, model.SelectedContainer!.ReceivedPerSecond);
            foreach (double width in new[] { 1388d, 900d, 600d })
            { await Layout(view, width, 700); Capture(view, $"docker-resources-{language}-{theme}-{width}"); }
            Assert.Empty(errors.Messages);
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); Text.Initialize(previous.Name, CultureInfo.InvariantCulture); themes.SetTheme(UiTheme.Light); }
    });

    private static async Task Layout(FrameworkElement view, double width, double height)
    { view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); view.UpdateLayout(); }
    private static void Inside(FrameworkElement child, FrameworkElement parent)
    {
        var at = child.TranslatePoint(new Point(), parent); Assert.True(child.ActualWidth > 0 && child.ActualHeight > 0);
        Assert.True(at.X >= -1 && at.Y >= -1 && at.X + child.ActualWidth <= parent.ActualWidth + 1 && at.Y + child.ActualHeight <= parent.ActualHeight + 1);
    }
    private static void Capture(FrameworkElement view, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR"); if (directory is null) return;
        Directory.CreateDirectory(directory); var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
    private sealed class Dialogs : ISshWorkspaceDialogs { public Task<bool> ConfirmCloseAsync(string instanceName) => Task.FromResult(false); }
    private sealed class Errors : TraceListener
    {
        internal List<string> Messages { get; } = [];
        public override void Write(string? message) { if (message?.Contains("Error:", StringComparison.Ordinal) == true) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
