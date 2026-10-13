namespace Asgard.Hermod.UI.Tests;

/// <summary>验证实例标签使用稳定标识，并与连接配置原子保存。</summary>
public sealed class InstanceTagPersistenceTests
{
    /// <summary>显式更新可替换或清空标签；未要求替换的调用继续保留原关联。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UpdateAsync_ReplacesTagsOnlyWhenRequested(bool replace, bool clear)
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var configurations = services.GetRequiredService<IInstanceConfigurationService>();
        var tags = services.GetRequiredService<ITagManagementService>();
        var first = await tags.SaveAsync(null, new("First", null, "#123456"), TestContext.Current.CancellationToken);
        var second = await tags.SaveAsync(null, new("Second", null, "#654321"), TestContext.Current.CancellationToken);
        var instance = await configurations.CreateAsync(Draft("Original", [first.Id]), TestContext.Current.CancellationToken);
        await tags.SaveAsync(first.Id, new("Renamed", null, "#123456"), TestContext.Current.CancellationToken);
        Assert.Equal([first.Id], await configurations.GetTagIdsAsync(instance.Id, TestContext.Current.CancellationToken));

        await configurations.UpdateAsync(instance.Id, Draft("Updated", clear ? [] : [second.Id, second.Id], replace),
            TestContext.Current.CancellationToken);
        Assert.Equal(replace ? clear ? [] : [second.Id] : [first.Id],
            await configurations.GetTagIdsAsync(instance.Id, TestContext.Current.CancellationToken));
        Assert.Equal("Updated", Assert.Single(configurations.GetInstances()).Name);
    }

    /// <summary>标签已经删除时整次更新失败，实例名称、凭据与原关联均不变化。</summary>
    [Fact]
    public async Task UpdateAsync_RejectsDeletedTagsWithoutPartialWrites()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var configurations = services.GetRequiredService<IInstanceConfigurationService>();
        var tags = services.GetRequiredService<ITagManagementService>();
        var tag = await tags.SaveAsync(null, new("Keep", null, "#123456"), TestContext.Current.CancellationToken);
        var instance = await configurations.CreateAsync(Draft("Original", [tag.Id]), TestContext.Current.CancellationToken);
        var repository = services.GetRequiredService<ILocalConfigurationRepository>();
        var before = repository.GetInstance(instance.Id, TestContext.Current.CancellationToken)!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => configurations.UpdateAsync(instance.Id,
            Draft("Must not save", ["deleted-tag"], true), TestContext.Current.CancellationToken));
        var after = repository.GetInstance(instance.Id, TestContext.Current.CancellationToken)!;
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.ProtectedParameters, after.ProtectedParameters);
        Assert.Equal(before.TagIds, after.TagIds);
    }

    /// <summary>构造有效的离线连接草稿，协议测试不访问任何云账号。</summary>
    private static InstanceConfigurationDraft Draft(string name, string[] ids, bool replace = false) => new()
    {
        Name = name,
        TagIds = ids,
        ReplaceTags = replace,
        Connection = new()
        {
            TypeId = "storage",
            ProviderId = "aliyun-oss",
            Parameters = new Dictionary<string, string> { ["accessKeyId"] = "TestAccessKey", ["accessKeySecret"] = "test-secret" },
        },
    };
}
