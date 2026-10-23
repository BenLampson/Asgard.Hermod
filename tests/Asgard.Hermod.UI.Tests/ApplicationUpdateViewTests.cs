using System.Globalization;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Views.Dialogs;
using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证左下角真实绑定、非模态详情导航和双语主题布局，不占用用户桌面。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ApplicationUpdateViewTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    [InlineData("fr-FR", UiTheme.Light)]
    public Task Indicator_OpensSharedDetailsWithoutClosingSession(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        try
        {
            var service = new UpdateTestService();
            using var updates = new ApplicationUpdateViewModel(service);
            var windows = new List<SettingsWindow>();
            using var editor = new SettingsEditorService(() => null, new TagManagementTestService(), new LanguagePreferenceTestStore(), updates: updates)
            { PresentWindow = window => windows.Add(window) };
            var terminals = new FakeTerminalSessionFactory();
            await using var shell = new ShellViewModel(new TestWorkspaceDataSource(), new FakeInstanceEditor(), terminals, themes, settings: editor, updates: updates);
            using var view = new AppShellView { DataContext = shell, Width = 1000, Height = 620 };
            Layout(view);
            var indicator = Assert.IsType<Button>(WindowTestVisuals.Named(view, "UpdateAvailableButton"));
            Assert.Equal(Visibility.Collapsed, indicator.Visibility);
            shell.Workspace.OpenLocalTerminal();
            var connection = shell.Workspace.SelectedTab;
            await updates.CheckAsync(); await WpfTestFixture.DrainDispatcherAsync(); Layout(view);
            Assert.Equal(Visibility.Visible, indicator.Visibility);
            Assert.Equal(Text.Get("UpdatesAvailableTooltip"), AutomationProperties.GetName(indicator));
            Assert.Same(shell.OpenUpdatesCommand, indicator.Command);
            Assert.InRange(indicator.TranslatePoint(new Point(), view).Y, 590, 620);
            await shell.OpenUpdatesCommand.ExecuteAsync();
            var window = Assert.Single(windows);
            Assert.Equal(0, new WindowInteropHelper(window).Handle);
            Assert.False(ComponentDispatcher.IsThreadModal);
            var settings = Assert.IsType<SettingsViewModel>(window.DataContext);
            Assert.True(settings.IsUpdatesSelected);
            Assert.Same(updates, settings.Updates);
            var settingsView = new SettingsView { DataContext = settings, Width = 780, Height = 550 };
            Layout(settingsView); await WpfTestFixture.DrainDispatcherAsync(); Layout(settingsView);
            var details = Assert.IsType<UpdateSettingsView>(settingsView.FindName("UpdatesContent"));
            Assert.Equal(Visibility.Visible, details.Visibility);
            Assert.Equal(updates.CurrentVersion, Assert.IsType<TextBlock>(details.FindName("InstalledVersion")).Text);
            Assert.Equal(updates.ReleaseNotes, Assert.IsType<TextBox>(details.FindName("ReleaseNotes")).Text);
            Assert.Same(updates.CheckCommand, Assert.IsType<Button>(details.FindName("CheckUpdates")).Command);
            Assert.True(Assert.IsType<TextBox>(details.FindName("ReleaseNotes")).IsReadOnly);
            await editor.OpenUpdatesAsync(); Assert.Same(window, windows.Last());
            Assert.Same(connection, shell.Workspace.SelectedTab);
            Assert.False(connection!.IsClosed);
            Assert.Equal(0, service.OpenCount);
            Capture(view, $"update-indicator-{language}-{theme}");
            Capture(settingsView, $"update-details-{language}-{theme}");
        }
        finally { themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
    }

    private static void Capture(FrameworkElement element, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)element.Width, (int)element.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
