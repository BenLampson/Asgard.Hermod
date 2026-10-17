using System.Globalization;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在真实 WPF 窗口验证双语绑定、设置导航及选择后的保存行为。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class LocalizationWindowTests(WpfTestFixture fixture)
{
    /// <summary>通用设置和标签管理均可进入，选择不会销毁已有工作区。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task Settings_RenderAndSaveInBothLanguages(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        SettingsWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var store = new LanguagePreferenceTestStore();
            using var model = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(), store);
            await model.InitializeAsync(TestContext.Current.CancellationToken);
            window = new SettingsWindow(model);
            WindowPreviewCapture.Show(window);
            await WindowPreviewCapture.VerifyAsync(window, "localization-settings-" + language);
            Assert.Equal(Text.Get("SystemSettings"), window.Title);
            var selector = Assert.Single(WindowTestVisuals.Descendants<ComboBox>(window), item => item.IsVisible);
            selector.SelectedItem = model.Languages.Single(item => item.Id == "en");
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal("en", store.Saved);
            Assert.Equal(Text.Get("LanguagePreferenceSavedRestartHermodToApplyIt"), model.LanguageMessage);
            model.SelectCategoryCommand.Execute(model.Categories.Single(item => item.Id == "tags"));
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(model.IsTagsSelected);
            Assert.False(selector.IsVisible);
            await WindowPreviewCapture.VerifyAsync(window, "localization-tags-" + language);
        }
        finally
        {
            window?.Close();
            CultureInfo.CurrentUICulture = previous;
        }
    });

    /// <summary>模态确认文案来自资源，而对象键作为用户数据原样保留。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task StorageConfirmation_PreservesObjectKey(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        ObjectStorageConfirmationWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var model = ObjectStorageConfirmationDialogViewModel.ForOverwrite("中文目录/用户文件.txt");
            window = new ObjectStorageConfirmationWindow(model);
            WindowPreviewCapture.Show(window);
            await WindowPreviewCapture.VerifyAsync(window, "localization-overwrite-" + language);
            Assert.Equal("中文目录/用户文件.txt", Assert.Single(model.Keys));
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(window), block => block.Text == Text.Get("OverwriteThisExistingObject"));
        }
        finally
        {
            window?.Close();
            CultureInfo.CurrentUICulture = previous;
        }
    });
}
