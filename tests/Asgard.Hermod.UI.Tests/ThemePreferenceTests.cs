using System.Globalization;
using Asgard.Hermod.UI;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证真实 SQLite、生产 UI 装配及主题命令的重启记忆和安全错误行为。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ThemePreferenceTests(WpfTestFixture fixture)
{
    /// <summary>所有主题入口立即持久化；重建容器后在创建控件之前恢复颜色。</summary>
    [Theory]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Light)]
    public Task ThemeCommands_RestoreSavedThemeBeforeViews(string language, UiTheme expected) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        using var storage = new LocalStorageTestEnvironment();
        try
        {
            using (var services = CreateServices(storage, new ResourceDictionary()))
            {
                services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
                Assert.Null(services.GetRequiredService<IThemePreferenceStore>().Load());
                var themes = services.GetRequiredService<IThemeService>();
                await using var shell = new ShellViewModel(new TestWorkspaceDataSource(), new FakeInstanceEditor(), new FakeTerminalSessionFactory(), themes);
                Assert.True(shell.IsLightTheme);
                var store = services.GetRequiredService<IThemePreferenceStore>();
                await shell.SetDarkThemeCommand.ExecuteAsync();
                Assert.Equal(UiTheme.Dark, store.Load());
                Assert.True(shell.IsDarkTheme);
                await shell.SetLightThemeCommand.ExecuteAsync();
                Assert.Equal(UiTheme.Light, store.Load());
                Assert.True(shell.IsLightTheme);
                if (expected == UiTheme.Dark) await shell.ToggleThemeCommand.ExecuteAsync();
                Assert.Equal(expected, store.Load());
                services.GetRequiredService<ILanguagePreferenceStore>().Save(language);
            }

            var resources = new ResourceDictionary();
            using var restarted = CreateServices(storage, resources);
            restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var restored = restarted.GetRequiredService<IThemeService>();
            Assert.Equal(expected, restored.Theme);
            Assert.Equal(language, restarted.GetRequiredService<ILanguagePreferenceStore>().Load());
            var control = new Border { Resources = resources };
            control.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            Assert.Equal(expected == UiTheme.Dark ? Color.FromRgb(0x30, 0x3C, 0x4D) : Colors.White,
                Assert.IsType<SolidColorBrush>(control.Background).Color);
            Assert.Single(resources.MergedDictionaries);
            Assert.Equal(LocalDatabaseInitializer.CurrentSchemaVersion,
                Convert.ToInt32(restarted.GetRequiredService<IFreeSql>().Ado.ExecuteScalar("PRAGMA user_version;"), CultureInfo.InvariantCulture));
        }
        finally { Text.SetApplicationCulture(null); }
    });

    /// <summary>未知主题兼容旧库，不写回默认值；非法枚举不能污染已有偏好。</summary>
    [Fact]
    public Task UnknownPreference_UsesDefaultWithoutOverwritingStorage() => fixture.RunOffscreenAsync(() =>
    {
        using var storage = new LocalStorageTestEnvironment();
        var resources = new ResourceDictionary();
        using var services = CreateServices(storage, resources);
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var database = services.GetRequiredService<IFreeSql>();
        database.Ado.ExecuteNonQuery("INSERT INTO app_setting (id, value, update_time) VALUES ('ui.theme', 'future-theme', '2026-01-01T00:00:00Z');");
        var store = services.GetRequiredService<IThemePreferenceStore>();
        Assert.Null(store.Load());
        Assert.Equal(UiTheme.Light, services.GetRequiredService<IThemeService>().Theme);
        Assert.Equal("future-theme", database.Ado.ExecuteScalar("SELECT value FROM app_setting WHERE id = 'ui.theme';"));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save((UiTheme)999));
        Assert.Null(store.Load());
        return Task.CompletedTask;
    });

    /// <summary>保存失败由命令捕获，保留颜色、绑定状态和旧偏好，并支持重试及英语回退。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public Task SaveFailure_PreservesThemeAndProvidesSafeRetry(string language) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var store = new ThemePreferenceTestStore();
            store.Save(UiTheme.Light);
            store.FailSave = true;
            var resources = new ResourceDictionary();
            var themes = new ThemeService(resources, store);
            var original = Assert.Single(resources.MergedDictionaries);
            int changes = 0;
            themes.ThemeChanged += (_, _) => changes++;
            await using var shell = new ShellViewModel(new TestWorkspaceDataSource(), new FakeInstanceEditor(), new FakeTerminalSessionFactory(), themes);
            await shell.ToggleThemeCommand.ExecuteAsync();
            var error = Assert.IsType<InvalidOperationException>(shell.ToggleThemeCommand.LastException);
            Assert.Equal(Text.Get("CannotSaveThemePreference"), Text.Error(error));
            Assert.DoesNotContain("private-", Text.Error(error));
            Assert.Equal(UiTheme.Light, store.Load());
            Assert.True(shell.IsLightTheme);
            Assert.Same(original, Assert.Single(resources.MergedDictionaries));
            Assert.Equal(0, changes);
            store.FailSave = false;
            await shell.ToggleThemeCommand.ExecuteAsync();
            Assert.Null(shell.ToggleThemeCommand.LastException);
            Assert.True(shell.IsDarkTheme);
            Assert.Equal(UiTheme.Dark, store.Load());
            Assert.Equal(1, changes);
        }
        finally { Text.SetApplicationCulture(null); }
    });

    /// <summary>使用生产注册入口，同时提供真实数据库和可独立检查的 WPF 资源集合。</summary>
    private static ServiceProvider CreateServices(LocalStorageTestEnvironment storage, ResourceDictionary resources) =>
        new ServiceCollection().AddHermodCore().AddWindowsSecretProtection().AddLocalStorage(storage.CreateConfiguration())
            .AddHermodUi(() => null, resources).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
}
