using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>其余三种真实模块转移相同标题元素；终端和对象存储均使用假边界。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceHeaderModulesHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task SshHeader_TransfersActionsWithoutReplacingTerminal(string language, UiTheme theme) => InCultureAsync(language, theme, async () =>
    {
        var factory = new SshTestFactory(); var dialogs = new SshTestDialogs { Accept = true };
        await using var page = new SshWorkspaceViewModel(new(new("ssh", "SSH", "SSH", "terminal", [], TypeId: SshIds.Type, ProviderId: SshIds.Direct)), new SshTestConfigurationService(), factory, dialogs, dialogs);
        await page.InitializeAsync(); var terminal = page.Session;
        using var host = new WorkspaceContentHost { Page = page }; AssertHeader(host, page, language, theme);
        Assert.Same(terminal, page.Session); Assert.True(page.IsConnected); Assert.Equal(1, factory.Calls);
    });

    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task StorageHeader_TransfersReadOnlyIdentityWithoutExtraReads(string language, UiTheme theme) => InCultureAsync(language, theme, async () =>
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service();
        await using var page = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage);
        await page.InitializeAsync(TestContext.Current.CancellationToken); int details = service.DetailsCalls.Count;
        using var host = new WorkspaceContentHost { Page = page }; AssertHeader(host, page, language, theme);
        Assert.True(page.HasConnectedOnce); Assert.Equal(details, service.DetailsCalls.Count); Assert.Empty(service.DownloadLimits);
        service.ListBucketsHandler = (_, _) => throw new IOException("password=private-test-secret");
        await page.ReconnectCommand.ExecuteAsync();
        Assert.True(page.Connection.HasError); Assert.False(page.Connection.IsOpen);
        Assert.DoesNotContain("private-test-secret", page.Connection.Error);
    });

    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task LocalHeader_TransfersWithoutStartingAnotherProcess(string language, UiTheme theme) => InCultureAsync(language, theme, async () =>
    {
        var factory = new FakeTerminalSessionFactory();
        await using var page = new TerminalWorkspaceViewModel("local", new(new("local", "Local", "Local", "terminal", [], true)), factory);
        var terminal = page.Session; using var host = new WorkspaceContentHost { Page = page }; AssertHeader(host, page, language, theme);
        Assert.Same(terminal, page.Session); Assert.True(page.IsRunning); Assert.Equal(1, factory.OpenAttempts);
    });

    private Task InCultureAsync(string language, UiTheme theme, Func<Task> test) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try { new ThemeService(Application.Current.Resources).SetTheme(theme); await test(); }
        finally { Text.SetApplicationCulture(null); }
    });

    private static void AssertHeader(WorkspaceContentHost host, WorkspacePageViewModel page, string language, UiTheme theme)
    {
        var view = Assert.IsAssignableFrom<FrameworkElement>(host.Content); var header = Assert.IsType<WorkspaceHeader>(host.TakeHeader());
        Assert.Same(page, header.DataContext); Assert.Null(host.TakeHeader());
        var root = new Border { Child = header }; root.Resources.MergedDictionaries.Add(Application.Current.Resources);
        header.ShowWindowControls = true;
        foreach (double width in new[] { 800d, 1600d })
        {
            root.Measure(new(width, 44)); root.Arrange(new(0, 0, width, 44)); root.UpdateLayout();
            Assert.Equal(44, header.ActualHeight); Assert.NotNull(header.StatusContent);
            var identity = Assert.IsType<WorkspaceIdentity>(header.IdentityContent);
            Assert.Equal(13, ((TextBlock)identity.FindName("InstanceName")).FontSize);
            Assert.Equal(11, ((TextBlock)identity.FindName("Address")).FontSize);
            Assert.Equal(width < 1000 ? Visibility.Collapsed : Visibility.Visible, ((TextBlock)identity.FindName("Address")).Visibility);
            Assert.Empty(WindowTestVisuals.Descendants<InstanceNameButton>(header));
            var icon = Assert.Single(WindowTestVisuals.Descendants<IconView>(identity)); Assert.Equal(22, icon.ActualWidth); Assert.Equal(22, icon.ActualHeight);
            string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                var bitmap = new RenderTargetBitmap((int)width, 44, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, $"caption-{page.GetType().Name}-{language}-{theme}-{width}.png")); encoder.Save(stream);
            }
        }
        Assert.Same(page, view.DataContext); Assert.Null(PresentationSource.FromVisual(root)); Assert.Null(Window.GetWindow(root));
    }
}
