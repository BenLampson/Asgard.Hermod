using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>关于内容在设置中完整可见，支持双语、窄窗口和源码地址复制。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AboutSettingsWindowTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    [InlineData("fr-FR", UiTheme.Light)]
    public Task About_ProvidesVersionLicenseAuthorAndExplicitRepositoryAction(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        using var model = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(), new LanguagePreferenceTestStore());
        var window = new SettingsWindow(model);
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var navigation = Assert.Single(WindowTestVisuals.Descendants<ListBox>(window), list => ReferenceEquals(list.ItemsSource, model.Categories));
            var category = model.Categories.Single(category => category.Id == "about");
            var entry = Assert.IsType<ListBoxItem>(navigation.ItemContainerGenerator.ContainerFromItem(category));
            entry.BringIntoView(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(entry.IsEnabled); Assert.True(entry.IsVisible); navigation.SelectedItem = category; await WindowTestVisuals.UpdateLayoutAsync(window);
            var about = Assert.Single(WindowTestVisuals.Descendants<AboutSettingsView>(window)); Assert.True(about.IsVisible); Assert.True(model.IsAboutSelected);
            Assert.Equal(new HomeWorkspaceViewModel(_ => { }).Version, Assert.IsType<TextBlock>(about.FindName("AboutVersion")).Text);
            foreach (string key in new[] { "HomeAuthor", "HomeLicense", "HomeOpenSourceDescription" })
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(about), block => block.Text == Text.Get(key) && block.IsVisible);
            var open = Assert.IsType<Button>(about.FindName("OpenRepository")); Assert.Same(model.About.OpenGitHubCommand, open.Command); Assert.True(open.IsEnabled);
            var address = Assert.IsType<TextBox>(about.FindName("RepositoryAddress")); Assert.True(address.IsReadOnly); Assert.Equal(model.About.RepositoryUrl, address.Text);
            Assert.Equal("https", new Uri(address.Text).Scheme); Assert.Equal("github.com", new Uri(address.Text).Host);
            Assert.Empty(WindowTestVisuals.Descendants<Expander>(about));
            await WindowPreviewCapture.VerifyAsync(window, $"settings-about-{language}-{theme}");
            window.Width = window.MinWidth; window.Height = window.MinHeight; await WindowPreviewCapture.VerifyAsync(window, $"settings-about-narrow-{language}-{theme}");
            var scroll = Assert.Single(WindowTestVisuals.Descendants<ScrollViewer>(about), item => item.Parent is AboutSettingsView);
            Assert.Equal(0, scroll.ScrollableWidth); Assert.True(scroll.ScrollableHeight > 0); scroll.ScrollToBottom(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(WindowTestVisuals.ScreenBounds(open).Right <= WindowTestVisuals.ScreenBounds(about).Right + 1);
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });
}
