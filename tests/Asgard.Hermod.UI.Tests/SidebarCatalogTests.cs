namespace Asgard.Hermod.UI.Tests;

/// <summary>验证标签管理与已有导航对象的联动，不依赖固定演示数据。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SidebarCatalogTests(WpfTestFixture fixture)
{
    /// <summary>保存通知先刷新侧栏时，创建回调复用同一个条目，不追加重复实例。</summary>
    [Fact]
    public Task Create_AfterSaveNotificationRefresh_UpdatesExistingItemOnce() => fixture.RunAsync(async () =>
    {
        var source = new ObjectStorageWorkspaceDataSource();
        var created = source.Instance with { Id = "new-instance", Name = "New instance" };
        var editor = new FakeInstanceEditor();
        InstanceItemViewModel? opened = null;
        InstanceItemViewModel? refreshed = null;
        using var sidebar = new InstanceSidebarViewModel(source, editor, item => opened = item, () => { });
        editor.CreateHandler = async () =>
        {
            source.Instance = created;
            await sidebar.RefreshAsync();
            refreshed = Assert.Single(sidebar.AllInstances);
            refreshed.IsSelected = true;
            return created;
        };
        await sidebar.AddInstanceCommand.ExecuteAsync();
        Assert.Null(sidebar.AddInstanceCommand.LastException);
        Assert.Same(refreshed, Assert.Single(sidebar.AllInstances));
        Assert.Same(refreshed, opened);
        Assert.True(opened!.IsSelected);
        Assert.Single(sidebar.Instances.Cast<InstanceItemViewModel>());
    });

    /// <summary>尚未关联实例的标签也出现，改名和改色按稳定标识保留过滤及选中态。</summary>
    [Fact]
    public Task Refresh_PreservesSelectionAndFollowsTagRenameAndColor() => fixture.RunAsync(async () =>
    {
        var tags = new SidebarTestTagService();
        using var sidebar = new InstanceSidebarViewModel(new SidebarCatalogDataSource(tags),
            new FakeInstanceEditor(), _ => { }, () => { }, tags);
        await sidebar.RefreshAsync();
        var unused = Assert.Single(sidebar.BuildTagFilterDefinitions(), node => node.Path == "Unused");
        Assert.Equal("#A855F7", unused.Color);
        sidebar.ApplyTagFilter(["Team/Platform"], TagFilterMatchMode.Any);
        sidebar.SelectInstance("oss");
        var instance = Assert.Single(sidebar.AllInstances);

        tags.Tags = tags.Tags.Select(tag => tag.Id == "platform"
            ? tag with { Name = "Core", Path = "Team/Core", Color = "#33AAFF" } : tag).ToArray();
        await sidebar.RefreshAsync();

        Assert.Same(instance, Assert.Single(sidebar.AllInstances));
        Assert.True(instance.IsSelected);
        Assert.Equal(new[] { "Team/Core" }, instance.Tags);
        Assert.Equal(new[] { "Team/Core" }, sidebar.SelectedTagPaths);
        Assert.Same(instance, Assert.Single(sidebar.Instances.Cast<InstanceItemViewModel>()));
        var team = Assert.Single(sidebar.BuildTagFilterDefinitions(), node => node.Path == "Team");
        Assert.Equal("#33AAFF", Assert.Single(team.Children).Color);
    });

    /// <summary>后台事务通知会回到 Dispatcher，删除标签清除关联并保留实例。</summary>
    [Fact]
    public Task BackgroundCatalogChange_RemovesFilterAndAssociationWithoutDeletingInstance() => fixture.RunAsync(async () =>
    {
        var tags = new SidebarTestTagService();
        using var sidebar = new InstanceSidebarViewModel(new SidebarCatalogDataSource(tags),
            new FakeInstanceEditor(), _ => { }, () => { }, tags);
        await sidebar.RefreshAsync();
        sidebar.ApplyTagFilter(["Team/Platform"], TagFilterMatchMode.All);
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sidebar.InstancesChanged += (_, _) => refreshed.TrySetResult();

        tags.Tags = tags.Tags.Where(tag => tag.Id != "platform").ToArray();
        await Task.Run(tags.PublishChanges);
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(sidebar.SelectedTagPaths);
        Assert.Empty(Assert.Single(sidebar.AllInstances).Tags);
        Assert.Equal("oss", Assert.Single(sidebar.Instances.Cast<InstanceItemViewModel>()).Id);
        Assert.DoesNotContain("Team/Platform", sidebar.AvailableTags);
        Assert.Equal("", sidebar.OperationError);
    });
}
