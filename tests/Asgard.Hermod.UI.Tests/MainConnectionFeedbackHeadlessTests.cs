using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.UI.Tests;

/// <summary>主窗体反馈的实际布局和初始化竞态均离屏验证，不构造任何窗口。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainConnectionFeedbackHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    public Task FirstFailureStaysInReferenceCardAndExplicitRetryCreatesFreshPage(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var source = new Source(); var pages = new Pages(); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
            await using var shell = new ShellViewModel(source, new FakeInstanceEditor(), new Terminals(), themes, pageFactory: pages);
            shell.Main.SelectedNavigation = shell.Main.Navigation[1];
            using var bindings = new WpfBindingTrace(); var view = new AppShellView { DataContext = shell };
            await Layout(view); Assert.Null(shell.Main.OpenFeedback);
            var main = Assert.Single(WindowTestVisuals.Descendants<MainWorkspaceView>(view));
            var overlay = (MainConnectionFeedback)main.FindName("OpenFeedback"); Assert.Equal(Visibility.Collapsed, overlay.Visibility);
            int activated = 0; shell.Workspace.PageActivated += _ => activated++;
            shell.Workspace.OpenInstance(new(source.GetInstances()[0])); var first = pages.Created[0];
            var feedback = Assert.IsType<MainConnectionFeedbackViewModel>(shell.Main.OpenFeedback); await Layout(view);
            Assert.Equal(Visibility.Visible, overlay.Visibility); Assert.Equal(0, activated);
            var card = (Border)overlay.FindName("ConnectionCard"); Assert.Equal(520, card.ActualWidth); Assert.Equal(new Thickness(26), card.Padding);
            Assert.Equal(new CornerRadius(8), card.CornerRadius); Assert.Equal(18, ((TextBlock)overlay.FindName("FeedbackTitle")).FontSize);
            Capture(view, $"main-opening-{language}-{theme}");
            first.Complete(false); await shell.Workspace.InitializePageAsync(first); await Layout(view);
            Assert.Same(feedback, shell.Main.OpenFeedback); Assert.True(feedback.HasError); Assert.Equal(1, first.Releases);
            Assert.Single(shell.Workspace.Tabs); Assert.Equal(0, activated); Assert.Equal("mysql.prod.internal:3306", feedback.Address);
            Assert.Equal(Visibility.Collapsed, shell.Main.ErrorVisibility); Assert.DoesNotContain("unsafe", feedback.Detail);
            Capture(view, $"main-open-failed-{language}-{theme}");
            feedback.RetryCommand.Execute(null); Assert.Equal(2, pages.Created.Count); Assert.False(feedback.RetryCommand.CanExecute(null));
            feedback.RetryCommand.Execute(null); Assert.Equal(2, pages.Created.Count);
            var next = pages.Created[1]; next.Complete(true); await shell.Workspace.InitializePageAsync(next); await Layout(view);
            Assert.Null(shell.Main.OpenFeedback); Assert.Equal(Visibility.Collapsed, overlay.Visibility); Assert.Equal(1, activated);
            Assert.Empty(bindings.Messages); Assert.Null(Window.GetWindow(view)); Assert.Null(PresentationSource.FromVisual(view));
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task CancelAndConcurrentSuccessNeverRetargetFeedbackOrActivateCanceledPage() => fixture.RunOffscreenAsync(async () =>
    {
        var source = new Source(); var pages = new Pages(); await using var workspace = new WorkspaceViewModel(source, new Terminals(), pages);
        int activated = 0; workspace.PageActivated += _ => activated++;
        workspace.OpenInstance(new(source.GetInstances()[0])); var first = pages.Created[0]; var old = workspace.OpenFeedback!;
        workspace.OpenInstance(new(source.GetInstances()[1])); var second = pages.Created[1];
        Assert.Same(old, workspace.OpenFeedback); old.CancelCommand.Execute(null);
        await workspace.InitializePageAsync(first); await WpfTestFixture.DrainDispatcherAsync();
        Assert.Equal(1, first.Cancels); Assert.Equal(1, first.Releases); Assert.Equal(0, activated);
        Assert.Equal("Audit PostgreSQL", workspace.OpenFeedback!.Name); Assert.False(old.CancelCommand.CanExecute(null));
        old.CancelCommand.Execute(null); Assert.Equal(0, second.Cancels);
        second.Complete(true); await workspace.InitializePageAsync(second); await WpfTestFixture.DrainDispatcherAsync();
        Assert.Null(workspace.OpenFeedback); Assert.Equal(1, activated); Assert.Equal(2, workspace.Tabs.Count);
        workspace.OpenInstance(new(source.GetInstances()[0])); var failed = pages.Created[^1]; failed.Complete(false);
        await workspace.InitializePageAsync(failed); await WpfTestFixture.DrainDispatcherAsync();
        var failure = workspace.OpenFeedback!; failure.DismissCommand.Execute(null); Assert.Null(workspace.OpenFeedback); Assert.Empty(workspace.OpenError);
        failure.RetryCommand.Execute(null); Assert.Equal(3, pages.Created.Count);
    });

    private sealed class Page : WorkspacePageViewModel
    {
        private readonly TaskCompletionSource<bool> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Releases, Cancels;
        internal Page(string id) : base(id, id, "database", id) { }
        public override WorkspaceConnectionViewModel ConnectionState { get; } = new();
        internal void Complete(bool success) => _pending.TrySetResult(success);
        public override async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ConnectionState.Begin(() => { Cancels++; _pending.TrySetCanceled(); });
            try
            {
                if (await _pending.Task) { HasConnectedOnce = true; ConnectionState.Finish(); }
                else ConnectionState.Finish(Text.Error(new IOException("unsafe connection-string=password")));
            }
            catch (OperationCanceledException) { ConnectionState.Finish(); }
        }
        protected override ValueTask ReleaseAsync() { Releases++; _pending.TrySetCanceled(); return ValueTask.CompletedTask; }
    }
    private sealed class Pages : IInstanceWorkspaceFactory
    {
        internal List<Page> Created { get; } = [];
        public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance) { var page = new Page(instance.Id); Created.Add(page); return page; }
    }
    private sealed class Source : IWorkspaceDataSource
    {
        public IReadOnlyList<InstanceSummary> GetInstances() => [new("production", "Production MySQL", "MySQL", "database", [], TypeId: "mysql", ProviderId: "mysql.direct") { ConnectionAddress = "mysql.prod.internal:3306" },
            new("audit", "Audit PostgreSQL", "PostgreSQL", "database", [], TypeId: "postgresql", ProviderId: "postgresql.direct") { ConnectionAddress = "postgres.prod.internal:5432" }];
        public ServiceSnapshot? GetServiceSnapshot(string instanceId) => null;
    }
    private sealed class Terminals : ITerminalSessionFactory
    {
        public ITerminalWorkspaceSession OpenLocal() => throw new InvalidOperationException();
    }
    private static async Task Layout(FrameworkElement view)
    {
        view.Resources.MergedDictionaries.Add(Application.Current.Resources); view.Measure(new(1360, 818)); view.Arrange(new(0, 0, 1360, 818)); view.UpdateLayout();
        await WpfTestFixture.DrainDispatcherAsync(); view.UpdateLayout();
    }
    private static void Capture(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (int scale in new[] { 1, 2 })
        {
            var bitmap = new RenderTargetBitmap(1360 * scale, 818 * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, $"{name}-{scale * 100}pct.png")); encoder.Save(stream);
        }
    }
}
