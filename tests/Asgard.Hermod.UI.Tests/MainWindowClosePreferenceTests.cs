namespace Asgard.Hermod.UI.Tests;

/// <summary>真实 SQLite 偏好使用稳定动作 ID，错误或旧数据安全回到首次询问。</summary>
public sealed class MainWindowClosePreferenceTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("\"text\"")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"ask\":false,\"action\":\"unknown\"}")]
    [InlineData("{\"ask\":\"false\",\"action\":\"hide\"}")]
    public void InvalidStoredValue_UsesAskAndHideDefaults(string value)
    {
        using var environment = new LocalStorageTestEnvironment(); using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        services.GetRequiredService<IFreeSql>().Ado.ExecuteNonQuery("INSERT INTO app_setting(id,value,update_time) VALUES('ui.mainWindow.close',@value,'test');", new { value });
        Assert.Equal(new(), services.GetRequiredService<IMainWindowClosePreferenceStore>().Load());
    }
    [Fact]
    public void Settings_PersistAcrossServiceRestartAndNotifyAfterCommit()
    {
        using var environment = new LocalStorageTestEnvironment();
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize(); var store = services.GetRequiredService<IMainWindowClosePreferenceStore>();
            Assert.Equal(new(), store.Load()); int changes = 0;
            store.Changed += (_, _) => { changes++; Assert.Equal(new(false, MainWindowCloseAction.ExitApplication), store.Load()); };
            store.Save(new(false, MainWindowCloseAction.ExitApplication)); Assert.Equal(1, changes);
        }
        using var restarted = environment.CreateServices(); restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        Assert.Equal(new(false, MainWindowCloseAction.ExitApplication), restarted.GetRequiredService<IMainWindowClosePreferenceStore>().Load());
    }
}
