
namespace Asgard.Hermod.UI.Tests;

/// <summary>验证持久化标签规则、稳定关联和子树删除对实例的真实影响。</summary>
public sealed class LocalTagManagementTests
{
    /// <summary>改名和移动层级不改变标识，实例路径由标签实时生成。</summary>
    [Fact]
    public async Task SaveAsync_PreservesAssociationsWhenRenamingAndMoving()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var tags = services.GetRequiredService<ITagManagementService>();
        ManagedTag parent = await tags.SaveAsync(null, new("Team", null, "#abc"), TestContext.Current.CancellationToken);
        ManagedTag child = await tags.SaveAsync(null, new("Platform", parent.Id, "#1a2b3c"), TestContext.Current.CancellationToken);
        Assert.Equal("#AABBCC", parent.Color);
        Assert.Equal("#1A2B3C", child.Color);
        InstanceSummary instance = await CreateInstanceAsync(services, [parent.Id, child.Id, child.Id]);

        ManagedTag renamed = await tags.SaveAsync(parent.Id, new("Teams", null, "#FFF"), TestContext.Current.CancellationToken);
        Assert.Equal(parent.Id, renamed.Id);
        Assert.Equal(1, renamed.InstanceCount);
        Assert.Contains("Teams/Platform", services.GetRequiredService<IInstanceConfigurationService>().GetInstances().Single().Tags);
        ManagedTag region = await tags.SaveAsync(null, new("Region", null, "#666"), TestContext.Current.CancellationToken);
        ManagedTag moved = await tags.SaveAsync(parent.Id, new("Teams", region.Id, "#FFF"), TestContext.Current.CancellationToken);
        Assert.Equal("Region/Teams", moved.Path);
        StoredInstanceConfiguration? stored = services.GetRequiredService<ILocalConfigurationRepository>()
            .GetInstance(instance.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(2, stored.TagIds.Count);
        Assert.Contains(parent.Id, stored.TagIds);
        Assert.Contains(child.Id, stored.TagIds);
    }

    /// <summary>同级大小写重复、非法颜色和循环层级不能写入；不同父级允许同名。</summary>
    [Fact]
    public async Task SaveAsync_RejectsInvalidNameColorAndParentRules()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var tags = services.GetRequiredService<ITagManagementService>();
        ManagedTag root = await tags.SaveAsync(null, new("Team", null, "#ABC"), TestContext.Current.CancellationToken);
        ManagedTag child = await tags.SaveAsync(null, new("Platform", root.Id, "#fff"), TestContext.Current.CancellationToken);
        foreach (TagEditDraft invalid in new[]
        {
            new TagEditDraft("team", null, "#fff"), new("Bad/Path", null, "#fff"),
            new("", null, "#fff"), new("Bad", null, "red"), new("Bad", null, "#12345"),
            new("Bad", "builtin.environment.production", "#fff"), new("Bad", "missing-parent", "#fff"),
            new("Environment", null, "#fff"),
        })
            await Assert.ThrowsAsync<InvalidOperationException>(() => tags.SaveAsync(null, invalid, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tags.SaveAsync(root.Id,
            new("Team", child.Id, "#fff"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tags.SaveAsync(root.Id,
            new("Team", root.Id, "#fff"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tags.SaveAsync("builtin.environment.production",
            new("Changed", null, "#fff"), TestContext.Current.CancellationToken));
        await tags.SaveAsync(null, new("Platform", null, "#fff"), TestContext.Current.CancellationToken);
        Assert.Equal(6, (await tags.GetTagsAsync(TestContext.Current.CancellationToken)).Count);
    }

    /// <summary>删除整个自定义子树按去重实例计数，移除关联但保留实例与凭据。</summary>
    [Fact]
    public async Task DeleteAsync_RemovesSubtreeAssociationsAndKeepsInstances()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var tags = services.GetRequiredService<ITagManagementService>();
        ManagedTag root = await tags.SaveAsync(null, new("Team", null, "#fff"), TestContext.Current.CancellationToken);
        ManagedTag child = await tags.SaveAsync(null, new("Platform", root.Id, "#fff"), TestContext.Current.CancellationToken);
        await CreateInstanceAsync(services, [root.Id, child.Id, "builtin.environment.production"]);
        await CreateInstanceAsync(services, [child.Id]);
        var before = await tags.GetTagsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, before.Single(tag => tag.Id == root.Id).InstanceCount);
        TagDeletionPreview preview = await tags.PreviewDeleteAsync(root.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, preview.AffectedInstanceCount);
        Assert.Equal(2, preview.Tags.Count);
        int notifications = 0;
        tags.TagsChanged += (_, _) => Interlocked.Increment(ref notifications);

        await tags.DeleteAsync(root.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, notifications);
        Assert.Equal(3, (await tags.GetTagsAsync(TestContext.Current.CancellationToken)).Count);
        var instances = services.GetRequiredService<IInstanceConfigurationService>();
        Assert.Equal(2, instances.GetInstances().Count);
        Assert.Single(instances.GetInstances().SelectMany(instance => instance.Tags));
        Assert.All(instances.GetInstances(), instance => Assert.DoesNotContain(instance.Tags, tag => tag.StartsWith("Team", StringComparison.Ordinal)));
        foreach (InstanceSummary instance in instances.GetInstances())
            Assert.NotNull(await instances.GetConnectionAsync(instance.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tags.DeleteAsync("builtin.environment.production", TestContext.Current.CancellationToken));
    }

    /// <summary>保存事务发现失效标签时必须回滚实例本身，不能留下半完成记录。</summary>
    [Fact]
    public async Task CreateInstanceAsync_RollsBackWhenSelectedTagWasDeleted()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateInstanceAsync(services, ["missing-tag"]));

        Assert.Empty(services.GetRequiredService<IInstanceConfigurationService>().GetInstances());
    }

    /// <summary>使用真实配置服务保存测试连接；不调用外部网络。</summary>
    private static Task<InstanceSummary> CreateInstanceAsync(ServiceProvider services, IReadOnlyList<string> tagIds) =>
        services.GetRequiredService<IInstanceConfigurationService>().CreateAsync(new InstanceConfigurationDraft
        {
            Name = "Test OSS",
            Connection = new InstanceConnectionOptions
            {
                TypeId = AliyunOssStorageProvider.TypeId,
                ProviderId = AliyunOssStorageProvider.ProviderId,
                Parameters = new Dictionary<string, string> { ["accessKeyId"] = "TestKey", ["accessKeySecret"] = "test-secret" },
            },
            TagIds = tagIds,
        }, TestContext.Current.CancellationToken);
}
