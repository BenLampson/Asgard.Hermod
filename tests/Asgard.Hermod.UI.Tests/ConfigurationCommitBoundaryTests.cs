using Asgard.Hermod.Infrastructure.Instances;
using Asgard.Hermod.Infrastructure.Security;
using Asgard.Hermod.Services;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证持久化提交与导航读取或视图通知失败之间的反馈边界。</summary>
public sealed class ConfigurationCommitBoundaryTests
{
    /// <summary>写入提交后即使仓储不能再读快照，保存仍成功返回提前准备的安全结果。</summary>
    [Fact]
    public async Task CreateAsync_ReturnsCommittedSummaryWithoutAdditionalRepositoryReads()
    {
        var repository = new CommitBoundaryConfigurationRepository();
        LocalInstanceConfigurationService service = CreateService(repository);

        InstanceSummary instance = await service.CreateAsync(CreateDraft(), TestContext.Current.CancellationToken);

        Assert.True(repository.HasCommitted);
        Assert.NotNull(repository.CreatedInstance);
        Assert.Equal(repository.CreatedInstance.Id, instance.Id);
        Assert.Equal("Boundary instance", instance.DisplayName);
        Assert.Equal(["Team"], instance.Tags);
        Assert.Equal(1, repository.SnapshotReadCount);
        Assert.DoesNotContain("private-token", instance.ToString());
    }

    /// <summary>准备导航结果失败发生在提交前，调用方重试时不会遇到一次被误报失败的保存。</summary>
    [Fact]
    public async Task CreateAsync_DoesNotCommitWhenPreparingSummaryFails()
    {
        var repository = new CommitBoundaryConfigurationRepository { FailBeforeCommit = true };
        LocalInstanceConfigurationService service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(CreateDraft(),
            TestContext.Current.CancellationToken));

        Assert.False(repository.HasCommitted);
        Assert.Null(repository.CreatedInstance);
        Assert.Equal(1, repository.SnapshotReadCount);
    }

    /// <summary>编辑保留身份和关联，提交后同样不依赖可能失败的导航读取。</summary>
    [Fact]
    public async Task UpdateAsync_ReturnsPreparedSummaryAndPreservesIdentity()
    {
        var original = new StoredInstanceConfiguration("existing", "Old name", "storage", "boundary-storage",
            [1, 2, 3], new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc), ["tag-team"]);
        var repository = new CommitBoundaryConfigurationRepository { ExistingInstance = original };
        LocalInstanceConfigurationService service = CreateService(repository);
        InstanceConfigurationDraft draft = CreateDraft();
        draft = new InstanceConfigurationDraft { Name = "Renamed", Connection = draft.Connection, TagIds = [] };

        InstanceSummary summary = await service.UpdateAsync(original.Id, draft, TestContext.Current.CancellationToken);

        Assert.True(repository.HasCommitted);
        Assert.NotNull(repository.CreatedInstance);
        Assert.Equal(original.Id, summary.Id);
        Assert.Equal("Renamed", summary.Name);
        Assert.Equal(["Team"], summary.Tags);
        Assert.Equal(original.CreatedAt, repository.CreatedInstance.CreatedAt);
        Assert.Equal(original.TagIds, repository.CreatedInstance.TagIds);
        Assert.Equal(1, repository.SnapshotReadCount);
    }

    /// <summary>保存和删除已提交后，首个订阅者抛出异常不能误报失败或阻断其他刷新订阅者。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TagChanges_PreserveSuccessfulFeedbackWhenSubscriberThrows(bool delete)
    {
        var repository = new CommitBoundaryConfigurationRepository();
        var service = new LocalTagManagementService(repository);
        int successfulNotifications = 0;
        service.TagsChanged += (_, _) => throw new InvalidOperationException("模拟视图刷新失败。");
        service.TagsChanged += (_, _) => Interlocked.Increment(ref successfulNotifications);

        if (delete)
            await service.DeleteAsync("tag-team", TestContext.Current.CancellationToken);
        else
        {
            ManagedTag result = await service.SaveAsync("tag-team", new("Updated team", null, "#FFFFFF"),
                TestContext.Current.CancellationToken);
            Assert.Same(repository.SavedTag, result);
        }

        Assert.True(repository.HasCommitted);
        Assert.Equal(delete, repository.HasDeletedTag);
        Assert.Equal(1, successfulNotifications);
    }

    /// <summary>使用生产凭据保护器，仓储故障在独立的内存替身边界注入。</summary>
    private static LocalInstanceConfigurationService CreateService(ILocalConfigurationRepository repository) => new(
        repository, new InstanceProviderRegistry([new FakeStorageProvider("boundary-storage")]),
        new WindowsDpapiSecretProtector());

    /// <summary>测试草稿包含标签和虚构凭据，足以验证返回快照在提交前完整准备。</summary>
    private static InstanceConfigurationDraft CreateDraft() => new()
    {
        Name = "Boundary instance",
        TagIds = ["tag-team"],
        Connection = new InstanceConnectionOptions
        {
            TypeId = "storage",
            ProviderId = "boundary-storage",
            Parameters = new Dictionary<string, string> { ["apiToken"] = "private-token" },
        },
    };
}
