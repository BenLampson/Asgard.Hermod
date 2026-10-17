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
    public Task Target_UpdatesInstanceMetadataWithoutChangingDatabaseOrDraft(string language) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var instance = new InstanceItemViewModel(new("redis", "Cache", "Cache", "database", [])
            { TagIds = ["builtin.environment.production"] });
            var session = new RedisTestSession();
            using var tab = new RedisKeyTabViewModel(session, 4, RedisKeyId.FromText("user:1042:profile"), new RedisTestDialogs(), instance);
            Assert.Equal(Text.Format("RedisWriteTarget", "Cache", Text.Get("Production"), 4), tab.TargetLabel);
            await tab.ReadAsync(); await tab.EditCommand.ExecuteAsync(); tab.EditorText = "unsaved key value";
            using var create = new RedisCreateViewModel(session, 7, instance: instance) { Key = "new:key", EditorText = "unsaved create value" };
            var notified = new List<string?>(); tab.PropertyChanged += (_, args) => notified.Add(args.PropertyName);
            int reads = session.Reads;
            instance.UpdateSnapshot(instance.Summary with { Name = "Renamed", TagIds = ["builtin.environment.development"] });
            Assert.Equal(Text.Format("RedisWriteTarget", "Renamed", Text.Get("Development"), 7), create.TargetLabel);
            Assert.Equal(Text.Format("RedisWriteTarget", "Renamed", Text.Get("Development"), 4), tab.TargetLabel);
            Assert.Contains(nameof(tab.TargetLabel), notified);
            Assert.Equal("unsaved key value", tab.EditorText); Assert.True(tab.IsDirty);
            Assert.Equal("unsaved create value", create.EditorText); Assert.Equal("new:key", create.Key);
            Assert.Equal(reads, session.Reads); Assert.Equal(0, session.Saves);
            Assert.Equal(4, tab.Database);
            Assert.Equal("user:1042:profile", tab.FullName);
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task Workspace_MetadataChangeKeepsSessionTabsAndUnsavedValues() => fixture.RunAsync(async () =>
    {
        var configurations = new RedisTestConfigurations(); var session = new RedisTestSession();
        var instance = new InstanceItemViewModel(RedisTestConfigurations.Instance with { TagIds = ["builtin.environment.production"] });
        await using var workspace = new RedisWorkspaceViewModel(instance, configurations, session, configurations, new RedisTestDialogs());
        await workspace.InitializeAsync(TestContext.Current.CancellationToken);
        await workspace.OpenAsync(RedisKeyId.FromText("user:1042:profile")); var first = workspace.ActiveTab!;
        await first.EditCommand.ExecuteAsync(); first.EditorText = "profile draft";
        workspace.DatabaseText = "3"; await workspace.QueryCommand.ExecuteAsync(); await workspace.OpenAsync(RedisKeyId.FromText("user:1042:session")); var second = workspace.ActiveTab!;
        await second.EditCommand.ExecuteAsync(); second.EditorText = "session draft";
        int reads = session.Reads, scans = session.Calls.Count; string pageId = workspace.Id;
        instance.UpdateSnapshot(instance.Summary with { Name = "Renamed cache", TagIds = ["builtin.environment.development"] });
        workspace.UpdateInstanceMetadata(instance.Summary);
        Assert.True(workspace.IsConnected); Assert.Equal(pageId, workspace.Id); Assert.Equal("Renamed cache", workspace.Header);
        Assert.Same(first, workspace.Tabs[0]); Assert.Same(second, workspace.ActiveTab);
        Assert.Equal(0, first.Database); Assert.Equal(3, second.Database); Assert.Equal("3", workspace.DatabaseText);
        Assert.Equal("profile draft", first.EditorText); Assert.Equal("session draft", second.EditorText);
        Assert.True(first.IsDirty); Assert.True(second.IsDirty);
        Assert.Equal(reads, session.Reads); Assert.Equal(scans, session.Calls.Count); Assert.Equal(0, session.Saves);
    });
}
