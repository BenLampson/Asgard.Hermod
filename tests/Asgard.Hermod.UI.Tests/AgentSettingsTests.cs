using System.Globalization;
using System.Text.Json;
using System.Windows.Automation;
using Asgard.Hermod.Agent;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>系统模型设置的持久化边界和真实密码控件均不得泄漏或复用错误服务的密钥。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentSettingsTests(WpfTestFixture fixture)
{
    /// <summary>真实 SQLite 只保存地址和模型，重开编辑器不回填密钥，重建进程配置也不恢复密钥。</summary>
    [Fact]
    public Task Save_PersistsOnlyNonSecretPreferencesAndReopeningNeverRefillsKey() => fixture.RunOffscreenAsync(() =>
    {
        using var environment = new LocalStorageTestEnvironment();
        var configuration = new AgentConfiguration();
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<IAgentPreferenceStore>();
            using var model = new AgentSettingsViewModel(configuration, store)
            { Endpoint = " https://models.example.test/v1 ", Model = " custom-model ", ApiKey = "private-key-do-not-persist" };
            model.SaveCommand.Execute(null);
            Assert.Equal("https://models.example.test/v1", configuration.Current.Endpoint);
            Assert.Equal("custom-model", configuration.Current.Model);
            Assert.Equal("private-key-do-not-persist", configuration.Current.ApiKey); Assert.Empty(model.ApiKey);
            string json = (string)services.GetRequiredService<IFreeSql>().Ado.ExecuteScalar("SELECT value FROM app_setting WHERE id = 'agent.model';");
            using var document = JsonDocument.Parse(json);
            Assert.Equal(new[] { "Endpoint", "Model" }, document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
            Assert.DoesNotContain("private-key", json, StringComparison.Ordinal); Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
            using var reopened = new AgentSettingsViewModel(configuration, store);
            Assert.Empty(reopened.ApiKey); Assert.Equal(configuration.Current.Endpoint, reopened.Endpoint);
        }
        using var restarted = environment.CreateServices(); restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var preferences = restarted.GetRequiredService<IAgentPreferenceStore>().Load();
        Assert.Equal(new AgentPreferences("https://models.example.test/v1", "custom-model"), preferences);
        var freshConfiguration = new AgentConfiguration(); freshConfiguration.Update(preferences.Endpoint, preferences.Model, "");
        using var fresh = new AgentSettingsViewModel(freshConfiguration, restarted.GetRequiredService<IAgentPreferenceStore>());
        Assert.Empty(fresh.ApiKey); Assert.Empty(freshConfiguration.Current.ApiKey);
        return Task.CompletedTask;
    });

    /// <summary>存储失败不替换当前传输快照，保留用户草稿，错误使用安全翻译及英语回退。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public Task SaveFailure_PreservesConfigurationAndDraftWithoutExposingStorageError(string language) => fixture.RunOffscreenAsync(() =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var configuration = new AgentConfiguration(); configuration.Update("https://old.example.test/v1", "old-model", "old-secret");
            var original = configuration.Current; var store = new PreferenceStore { FailSave = true };
            using var model = new AgentSettingsViewModel(configuration, store)
            { Endpoint = "https://new.example.test/v1", Model = "new-model", ApiKey = "new-secret" };
            model.SaveCommand.Execute(null);
            Assert.Same(original, configuration.Current); Assert.Equal("new-secret", model.ApiKey); Assert.Null(store.Saved);
            Assert.Equal(Text.Get("AgentSettingsSaveFailed"), model.Status);
            Assert.DoesNotContain("private", model.Status, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("secret", model.Status, StringComparison.OrdinalIgnoreCase);
            if (language == "fr-FR")
            {
                string fallback = model.Status; Text.SetApplicationCulture(CultureInfo.GetCultureInfo("en"));
                Assert.Equal(Text.Get("AgentSettingsSaveFailed"), fallback);
            }
            store.FailSave = false; model.SaveCommand.Execute(null);
            Assert.Equal("https://new.example.test/v1", configuration.Current.Endpoint); Assert.Equal("new-secret", configuration.Current.ApiKey);
            Assert.Empty(model.ApiKey); Assert.Equal(new AgentPreferences(model.Endpoint, model.Model), store.Saved);
        }
        finally { Text.SetApplicationCulture(null); }
        return Task.CompletedTask;
    });

    /// <summary>同服务空密钥保留本次运行凭据，切换地址必须清除旧密钥；清除和关闭编辑器不会把密钥写入偏好。</summary>
    [Fact]
    public Task EndpointChangeAndExplicitClear_KeepKeysScopedToCurrentService() => fixture.RunOffscreenAsync(() =>
    {
        var configuration = new AgentConfiguration(); configuration.Update("https://first.example.test/v1", "model-a", "first-key");
        var store = new PreferenceStore(); using var model = new AgentSettingsViewModel(configuration, store);
        model.Model = "model-b"; model.SaveCommand.Execute(null); Assert.Equal("first-key", configuration.Current.ApiKey);
        model.Endpoint = "https://second.example.test/v1"; model.SaveCommand.Execute(null);
        Assert.Empty(configuration.Current.ApiKey); Assert.Equal("https://second.example.test/v1", configuration.Current.Endpoint);
        model.ApiKey = "second-key"; model.SaveCommand.Execute(null); Assert.Equal("second-key", configuration.Current.ApiKey);
        int saves = store.SaveCount; model.ApiKey = "unfinished-draft"; model.ClearKeyCommand.Execute(null);
        Assert.Empty(configuration.Current.ApiKey); Assert.Empty(model.ApiKey); Assert.Equal(saves, store.SaveCount);
        Assert.Equal("model-b", configuration.Current.Model); Assert.Equal("https://second.example.test/v1", configuration.Current.Endpoint);
        model.ApiKey = "unsaved-key"; model.Dispose(); Assert.Empty(model.ApiKey); Assert.Equal(saves, store.SaveCount);
        return Task.CompletedTask;
    });

    /// <summary>非法地址在存储之前被拒绝，凭据和配置都不能被半更新。</summary>
    [Theory]
    [InlineData("http://remote.example.test/v1")]
    [InlineData("https://user:private-password@example.test/v1")]
    [InlineData("https://example.test/v1?key=private-password")]
    public Task InvalidEndpoint_NeverCommitsOrLeaksInput(string endpoint) => fixture.RunOffscreenAsync(() =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("en"));
        try
        {
            var configuration = new AgentConfiguration(); configuration.Update("https://old.example.test/v1", "old-model", "old-secret");
            var original = configuration.Current; var store = new PreferenceStore();
            using var model = new AgentSettingsViewModel(configuration, store) { Endpoint = endpoint, ApiKey = "new-secret" };
            model.SaveCommand.Execute(null);
            Assert.Same(original, configuration.Current); Assert.Equal(0, store.SaveCount); Assert.Equal("new-secret", model.ApiKey);
            Assert.Equal(Text.Get("AgentInvalidEndpoint"), model.Status); Assert.DoesNotContain("private-password", model.Status);
        }
        finally { Text.SetApplicationCulture(null); }
        return Task.CompletedTask;
    });

    /// <summary>真实 WPF 设置页在两种语言主题中复用遮蔽控件，保存后立即清空控件副本。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task SettingsView_UsesLocalizedLabelsAndMaskedNonRefilledKey(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace(); Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var configuration = new AgentConfiguration(); configuration.Update("https://models.example.test/v1", "my-model", "existing-private-key");
        using var model = new AgentSettingsViewModel(configuration, new PreferenceStore());
        var view = new AgentSettingsView { DataContext = model }; var window = new HermodWindow { Content = view, Width = 720, Height = 650 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var secret = Assert.Single(WindowTestVisuals.Descendants<SecretInput>(view)); Assert.Empty(secret.Value); Assert.False(secret.IsPasswordVisible);
            Assert.Equal(Text.Get("AgentApiKey"), AutomationProperties.GetName(secret));
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), label => label.Text == Text.Get("AgentSettingsTitle"));
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), label => label.Text == Text.Get("AgentKeyInMemory"));
            var password = Assert.IsType<PasswordBox>(secret.Template.FindName("PART_Editor", secret));
            password.Password = "new-private-key"; Assert.Equal("new-private-key", model.ApiKey); Assert.False(secret.IsPasswordVisible);
            model.SaveCommand.Execute(null); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Empty(model.ApiKey); Assert.Empty(secret.Value); Assert.Empty(password.Password);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBlock>(view), label => label.Text.Contains("private-key", StringComparison.Ordinal));
            Assert.Empty(trace.Messages);
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });

    private sealed class PreferenceStore : IAgentPreferenceStore
    {
        internal AgentPreferences? Saved;
        internal bool FailSave;
        internal int SaveCount;
        public AgentPreferences Load() => Saved ?? new();
        public void Save(AgentPreferences preferences)
        {
            if (FailSave) throw new IOException("private storage path and secret");
            SaveCount++; Saved = preferences;
        }
    }
}
