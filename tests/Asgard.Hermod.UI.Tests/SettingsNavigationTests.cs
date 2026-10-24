using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>所有已实现设置在左侧直接导航，关于最后，未实现入口不占位。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SettingsNavigationTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task AvailablePagesUseOneLeftNavigationWithAboutLast(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        using var bindings = new WpfBindingTrace(); using var updates = new ApplicationUpdateViewModel(new UpdateTestService());
        using var model = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(),
            languageStore: new LanguagePreferenceTestStore(), fileRules: new FileOpenRuleTestStore(), temporaryFiles: new StorageSettingsTestStorage(),
            storageDialogs: new StorageSettingsTestDialogs(), accounts: new AccountUiTestService(), openAccount: () => Task.CompletedTask, updates: updates);
        var view = new SettingsView { DataContext = model };
        try
        {
            await model.InitializeAsync(); await SshKubernetesTests.Layout(view, 1020, 740);
            var navigation = Assert.IsType<ListBox>(view.FindName("SettingsNavigation"));
            Assert.Equal(new[] { "general", "tags", "storage", "file-opening", "account", "updates", "about" }, model.Categories.Select(category => category.Id));
            Assert.All(model.Categories, category => Assert.True(category.IsAvailable));
            foreach (var category in model.Categories)
            {
                navigation.SelectedItem = category; await SshKubernetesTests.Layout(view, 800, 480);
                Assert.Same(category, model.SelectedCategory);
                var item = Assert.IsType<ListBoxItem>(navigation.ItemContainerGenerator.ContainerFromItem(category));
                Assert.True(item.IsSelected); Assert.InRange(item.TranslatePoint(new(), view).X + item.ActualWidth, 1, 190);
                Assert.InRange(item.TranslatePoint(new(), view).Y + item.ActualHeight, 1, 480);
            }
            navigation.SelectedItem = model.Categories[0]; await SshKubernetesTests.Layout(view, 1020, 740);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBlock>(view), text => text.Text == Text.Get("AdditionalSettings"));
            SshKubernetesTests.Capture(view, $"settings-navigation-{language}-{theme}");
            Assert.Empty(bindings.Messages);
        }
        finally { Text.SetApplicationCulture(null); themes.SetTheme(UiTheme.Light); }
    });
}
