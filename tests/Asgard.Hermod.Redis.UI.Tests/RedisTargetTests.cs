using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>写入目标包含实例与环境，并保留页签原数据库。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class RedisTargetTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task Target_UsesTabDatabaseAndCreateSnapshot(string language) => fixture.RunAsync(() =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var instance = new InstanceItemViewModel(new("redis", "Cache", "Cache", "database", [])
            { TagIds = ["builtin.environment.production"] });
            var session = new RedisTestSession();
            using var tab = new RedisKeyTabViewModel(session, 4, RedisKeyId.FromText("user:1042:profile"), new RedisTestDialogs(), instance);
            Assert.Equal(Text.Format("RedisWriteTarget", "Cache", Text.Get("Production"), 4), tab.TargetLabel);
            using var create = new RedisCreateViewModel(session, 7, target: Text.Format("RedisWriteTarget", instance.Name, instance.EnvironmentLabel, 7));
            instance.UpdateSnapshot(instance.Summary with { Name = "Renamed", TagIds = ["builtin.environment.development"] });
            Assert.Equal(Text.Format("RedisWriteTarget", "Cache", Text.Get("Production"), 7), create.TargetLabel);
            Assert.Equal(4, tab.Database);
            Assert.Equal("user:1042:profile", tab.FullName);
        }
        finally { Text.SetApplicationCulture(null); }
        return Task.CompletedTask;
    });
}
