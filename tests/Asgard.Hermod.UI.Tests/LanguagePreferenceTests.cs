using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证设置选择、错误恢复与真实数据库重新打开后的语言记忆。</summary>
public sealed class LanguagePreferenceTests
{
    /// <summary>选择立即保存，重开设置仍选中该语种；恢复跟随系统保存空偏好。</summary>
    [Fact]
    public void Settings_SaveAndRestoreManualLanguage()
    {
        var store = new LanguagePreferenceTestStore();
        using (var model = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(), store))
        {
            Assert.True(model.IsGeneralSelected);
            Assert.Equal("", model.SelectedLanguage.Id);
            model.SelectedLanguage = model.Languages.Single(language => language.Id == "en");
            Assert.Equal("en", store.Saved);
            Assert.Equal(Text.Get("LanguagePreferenceSavedRestartHermodToApplyIt"), model.LanguageMessage);
        }
        using var reopened = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(), store);
        Assert.Equal("en", reopened.SelectedLanguage.Id);
        reopened.SelectedLanguage = reopened.Languages[0];
        Assert.Null(store.Saved);
    }

    /// <summary>保存失败保留原来的选择和数据库值，不展示原始异常信息。</summary>
    [Fact]
    public void Settings_SaveFailurePreservesSelection()
    {
        var store = new LanguagePreferenceTestStore { FailSave = true };
        using var model = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(), store);
        var original = model.SelectedLanguage;
        model.SelectedLanguage = model.Languages.Single(language => language.Id == "en");
        Assert.Same(original, model.SelectedLanguage);
        Assert.Null(store.Saved);
        Assert.Equal(Text.Get("CannotSaveTheLanguagePreferenceTryAgain"), model.LanguageMessage);
        Assert.DoesNotContain("private-database-path", model.LanguageMessage);
    }

    /// <summary>使用实际 SQLite，重新创建服务容器后手动选择仍被保留。</summary>
    [Fact]
    public void Sqlite_PreservesPreferenceAcrossReopeningAndSystemReset()
    {
        using var environment = new LocalStorageTestEnvironment();
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<ILanguagePreferenceStore>();
            Assert.Null(store.Load());
            store.Save("en");
        }
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<ILanguagePreferenceStore>();
            Assert.Equal("en", store.Load());
            store.Save("zh-Hans");
            Assert.Equal("zh-Hans", store.Load());
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Save("unsupported-language"));
            Assert.Equal("zh-Hans", store.Load());
            store.Save(null);
        }
        using var reopened = environment.CreateServices();
        reopened.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        Assert.Equal("", reopened.GetRequiredService<ILanguagePreferenceStore>().Load());
    }
}
