namespace Asgard.Hermod.UI.Tests;

/// <summary>真实 SQLite app_setting 提示标记不随进程重启或语言变更丢失。</summary>
public sealed class TrayPreferenceTests
{
    /// <summary>首次标记立即保存，重新装配服务后仍然有效且不触碰语言偏好。</summary>
    [Fact]
    public void HiddenHint_PersistsAcrossRestart()
    {
        using var environment = new LocalStorageTestEnvironment();
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<ITrayPreferenceStore>();
            Assert.False(store.HasShownHideHint());
            store.MarkHideHintShown(); store.MarkHideHintShown();
            Assert.True(store.HasShownHideHint());
            services.GetRequiredService<ILanguagePreferenceStore>().Save("en");
        }
        using var restarted = environment.CreateServices();
        restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        Assert.True(restarted.GetRequiredService<ITrayPreferenceStore>().HasShownHideHint());
        Assert.Equal("en", restarted.GetRequiredService<ILanguagePreferenceStore>().Load());
    }
}
