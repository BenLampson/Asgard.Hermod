using Asgard.Hermod.Abstractions.Security;

namespace Asgard.Hermod.UI.Tests;

/// <summary>轻量元数据修改使用真实 SQLite，验证密文、稳定身份和重启恢复。</summary>
public sealed class InstanceMetadataPersistenceTests
{
    [Fact]
    public async Task RenameAndTags_PersistWithoutReadingOrRewritingCredentials()
    {
        using var environment = new LocalStorageTestEnvironment();
        string id, tagId;
        byte[] ciphertext;
        DateTime created;
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var tags = services.GetRequiredService<ITagManagementService>();
            var tag = await tags.SaveAsync(null, new("Team", null, "#123456"), TestContext.Current.CancellationToken);
            tagId = tag.Id;
            var configurations = services.GetRequiredService<IInstanceConfigurationService>();
            id = (await configurations.CreateAsync(Draft([tag.Id]), TestContext.Current.CancellationToken)).Id;
            var repository = services.GetRequiredService<ILocalConfigurationRepository>();
            var before = repository.GetInstance(id, TestContext.Current.CancellationToken)!;
            ciphertext = before.ProtectedParameters.ToArray(); created = before.CreatedAt;
            var metadata = new LocalInstanceConfigurationService(repository, services.GetRequiredService<IInstanceProviderRegistry>(), new RejectSecretAccess());
            var renamed = await metadata.UpdateMetadataAsync(id, new() { Name = "  Renamed instance  " }, TestContext.Current.CancellationToken);
            Assert.Equal("Renamed instance", renamed.Name);
            Assert.Equal([tagId], renamed.TagIds);
            Assert.Equal(id, renamed.Id);
            await metadata.UpdateMetadataAsync(id, new() { TagIds = ["builtin.environment.production"] }, TestContext.Current.CancellationToken);
            var after = repository.GetInstance(id, TestContext.Current.CancellationToken)!;
            Assert.Equal(ciphertext, after.ProtectedParameters);
            Assert.Equal(created, after.CreatedAt);
            Assert.Equal(before.TypeId, after.TypeId); Assert.Equal(before.ProviderId, after.ProviderId);
            await tags.RenameAsync(tagId, "Renamed team", TestContext.Current.CancellationToken);
            await metadata.UpdateMetadataAsync(id, new() { TagIds = [tagId] }, TestContext.Current.CancellationToken);
        }
        using var reopened = environment.CreateServices();
        reopened.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var persisted = reopened.GetRequiredService<ILocalConfigurationRepository>().GetInstance(id, TestContext.Current.CancellationToken)!;
        Assert.Equal("Renamed instance", persisted.Name);
        Assert.Equal(ciphertext, persisted.ProtectedParameters);
        Assert.Equal(created, persisted.CreatedAt);
        Assert.Equal([tagId], persisted.TagIds);
        var summary = Assert.Single(reopened.GetRequiredService<IInstanceConfigurationService>().GetInstances());
        Assert.Equal(["Renamed team"], summary.Tags);
    }

    [Fact]
    public async Task InvalidTagsOrCancellation_RollBackAllMetadata()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var configurations = services.GetRequiredService<IInstanceConfigurationService>();
        var created = await configurations.CreateAsync(Draft([]), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => configurations.UpdateMetadataAsync(created.Id,
            new() { Name = "Must not persist", TagIds = ["deleted-tag"] }, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => configurations.UpdateMetadataAsync(created.Id,
            new() { Name = "Cancelled" }, cancellation.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => configurations.UpdateMetadataAsync(created.Id, new() { Name = "\n" }, TestContext.Current.CancellationToken));
        Assert.Equal(created.Name, Assert.Single(configurations.GetInstances()).Name);
    }

    [Fact]
    public async Task TagRename_PreservesCurrentParentColorAndDescendantLinks()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var tags = services.GetRequiredService<ITagManagementService>();
        var parent = await tags.SaveAsync(null, new("Parent", null, "#123456"), TestContext.Current.CancellationToken);
        var child = await tags.SaveAsync(null, new("Child", parent.Id, "#654321"), TestContext.Current.CancellationToken);
        var configurations = services.GetRequiredService<IInstanceConfigurationService>();
        var instance = await configurations.CreateAsync(Draft([child.Id]), TestContext.Current.CancellationToken);
        await tags.SaveAsync(parent.Id, new("Parent", null, "#abcdef"), TestContext.Current.CancellationToken);
        var renamed = await tags.RenameAsync(parent.Id, "New parent", TestContext.Current.CancellationToken);
        Assert.Equal(parent.Id, renamed.Id); Assert.Equal("#ABCDEF", renamed.Color.ToUpperInvariant());
        Assert.Equal(parent.ParentId, renamed.ParentId);
        Assert.Equal([child.Id], await configurations.GetTagIdsAsync(instance.Id, TestContext.Current.CancellationToken));
        Assert.Equal(["New parent/Child"], Assert.Single(configurations.GetInstances()).Tags);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tags.RenameAsync("builtin.environment.production", "Custom", TestContext.Current.CancellationToken));
    }

    private static InstanceConfigurationDraft Draft(string[] ids) => new()
    {
        Name = "Original",
        TagIds = ids,
        Connection = new()
        {
            TypeId = "storage",
            ProviderId = "aliyun-oss",
            Parameters = new Dictionary<string, string> { ["accessKeyId"] = "TestAccessKey", ["accessKeySecret"] = "test-secret" }
        }
    };

    private sealed class RejectSecretAccess : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext) => throw new InvalidOperationException("Metadata must not encrypt credentials.");
        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) => throw new InvalidOperationException("Metadata must not decrypt credentials.");
    }
}
