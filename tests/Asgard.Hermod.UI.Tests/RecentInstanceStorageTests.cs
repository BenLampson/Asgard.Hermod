namespace Asgard.Hermod.UI.Tests;

/// <summary>最近访问保持本机持久化、数量上限与损坏记录恢复。</summary>
public sealed class RecentInstanceStorageTests
{
    /// <summary>使用真实 SQLite 重启读取，不把名称、地址或凭据写入最近访问。</summary>
    [Fact]
    public void RecentHistory_PersistsAcrossRestartAndRecoversFromCorruption()
    {
        using var environment = new LocalStorageTestEnvironment();
        var entries = Enumerable.Range(0, 7).Select(index => new RecentInstance("instance-" + index,
            DateTimeOffset.Parse("2026-10-13T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture).AddMinutes(-index))).ToArray();
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<IRecentInstanceStore>();
            Assert.Empty(store.Load());
            store.Save(entries);
        }
        using var restarted = environment.CreateServices();
        restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var restored = restarted.GetRequiredService<IRecentInstanceStore>();
        Assert.Equal(entries.Take(5), restored.Load());
        restarted.GetRequiredService<IFreeSql>().Ado.ExecuteNonQuery(
            "UPDATE app_setting SET value = 'broken json' WHERE id = 'workspace.recent';");
        Assert.Empty(restored.Load());
        restored.Save([entries[1], entries[0], entries[1]]);
        Assert.Equal(new[] { entries[0], entries[1] }, restored.Load());
    }
}
