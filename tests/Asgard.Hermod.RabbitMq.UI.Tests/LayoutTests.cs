using System.Windows.Media.Imaging;
using Asgard.Hermod.RabbitMq.UI.Controls;
using Asgard.Hermod.UI.Tests;
using Asgard.Hermod.UI.Themes;

namespace Asgard.Hermod.RabbitMq.UI.Tests;

[Collection(HeadlessCollection.Name)]
public sealed class LayoutTests(HeadlessFixture fixture)
{
    [Theory]
    [InlineData("zh-Hans", UiTheme.Light, 1440)]
    [InlineData("en", UiTheme.Dark, 1440)]
    [InlineData("en", UiTheme.Light, 1120)]
    public Task Workspace_HasNoBindingErrors_AtDesignSizes(string language, UiTheme theme, double width) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        using var trace = new WpfBindingTrace();
        await using var model = WorkspaceTests.Model(new(), new()); await model.InitializeAsync();
        model.PageSize = 6;
        await model.NavigateCommand.ExecuteAsync(model.Navigation.Single(item => item.Resource == RabbitMqResource.Queues));
        var view = new RabbitMqWorkspaceView { DataContext = model }; var root = Root(view); await Layout(root, width, 900);
        var header = Assert.Single(Descendants<Asgard.Hermod.UI.Controls.WorkspaceHeader>(root));
        Assert.Equal(44, header.ActualHeight);
        Assert.Equal(0, header.TransformToAncestor(root).Transform(new Point()).Y);
        var environment = Assert.Single(Descendants<Asgard.Hermod.UI.Controls.StatusBadge>(header));
        Assert.Equal(Text.Get("Production"), environment.Text);
        Assert.True(environment.ActualWidth > 20);
        Assert.Equal(36, Assert.IsType<RabbitMqTable>(view.FindName("ResourceTable")).RowHeight);
        Assert.Equal(6, model.Rows.Count); Assert.Equal(2, model.PageCount);
        Assert.Equal(254, Assert.IsType<RabbitMqTable>(view.FindName("ResourceTable")).ActualHeight);
        Assert.Equal(8, Assert.IsType<RabbitMqTable>(view.FindName("ResourceTable")).Columns.Count);
        model.SelectedRow = model.Rows[0]; await Layout(root, width, 900);
        Capture(root, $"queues-{language}-{theme}-{width}");
        model.SelectedRow = model.Rows[0]; await model.OpenCommand.ExecuteAsync(); await Layout(root, width, 900); Capture(root, $"details-{language}-{theme}-{width}");
        await model.MessagesCommand.ExecuteAsync(); await model.GetCommand.ExecuteAsync(); await Layout(root, width, 900); Capture(root, $"messages-{language}-{theme}-{width}");
        await model.BackCommand.ExecuteAsync(); await model.NavigateCommand.ExecuteAsync(model.AdministrationPages[0]); await Layout(root, width, 900); Capture(root, $"administration-{language}-{theme}-{width}");
        await model.NavigateCommand.ExecuteAsync(model.Navigation[0]); await Layout(root, width, 900); Capture(root, $"overview-{language}-{theme}-{width}");
        if (trace.Messages.Any(message => message.Contains("Error:", StringComparison.Ordinal))) Assert.Fail(string.Join(Environment.NewLine, trace.Messages.Distinct()));
    });
    [Theory]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    public Task Forms_RenderWithoutWindow_AndKeepChoiceBindings(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        using var trace = new WpfBindingTrace();
        foreach (var operation in new[] { RabbitMqOperation.DeclareQueue, RabbitMqOperation.DeclareExchange, RabbitMqOperation.CreateBinding, RabbitMqOperation.PutVhost, RabbitMqOperation.PutUser, RabbitMqOperation.PutPermissions, RabbitMqOperation.PutTopicPermissions, RabbitMqOperation.PutPolicy, RabbitMqOperation.Publish })
        {
            var form = new RabbitMqForm(operation, "/production", "orders", Text.Format("RabbitMqWriteTarget", "Production", "Production", "https://mq.example.test:15671", "/production"), _ => Task.CompletedTask);
            var root = Root(new RabbitMqFormView { DataContext = form }); await Layout(root, form.Width, form.Height - 96); Capture(root, $"form-{operation}-{language}-{theme}");
            foreach (var combo in Descendants<ComboBox>(root)) if (combo.Items.Count > 0) Assert.NotNull(combo.SelectedItem);
        }
        if (trace.Messages.Any(message => message.Contains("Error:", StringComparison.Ordinal))) Assert.Fail(string.Join(Environment.NewLine, trace.Messages.Distinct()));
    });
    private static Border Root(UIElement child)
    { var root = new Border { Child = child }; root.Resources.MergedDictionaries.Add(Application.Current.Resources); root.SetResourceReference(Border.BackgroundProperty, "RabbitMqBackgroundBrush"); return root; }
    private static async Task Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout();
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        root.UpdateLayout(); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        { var child = VisualTreeHelper.GetChild(root, index); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static void Capture(FrameworkElement root, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
