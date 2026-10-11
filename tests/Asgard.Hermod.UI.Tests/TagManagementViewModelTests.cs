namespace Asgard.Hermod.UI.Tests;

/// <summary>验证固定环境、搜索上下文、刷新选择和编辑服务协调。</summary>
public sealed class TagManagementViewModelTests
{
    /// <summary>环境只读且固定顺序，自定义数量不包含环境。</summary>
    [Fact]
    public async Task Initialize_SeparatesFixedEnvironmentAndCustomTree()
    {
        var service = new TagManagementTestService();
        using var model = new TagManagementViewModel(service, new TagDialogTestService());
        await model.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "Production", "Staging", "Development" }, model.BuiltInTags.Select(tag => tag.Name));
        Assert.Equal(6, model.CustomTagCount);
        Assert.Equal(6, model.Rows.Count);
        Assert.DoesNotContain(model.Rows, node => node.Tag.IsBuiltIn);
        Assert.False(model.EditCommand.CanExecute(model.BuiltInTags[0]));
        Assert.False(model.AddChildCommand.CanExecute(model.BuiltInTags[0]));
        Assert.False(model.DeleteCommand.CanExecute(model.BuiltInTags[0]));
        Assert.True(model.NewTagCommand.CanExecute(null));
    }

    /// <summary>深层检索自动展开上下文，清空后恢复折叠，隐藏选中项仍保留详情。</summary>
    [Fact]
    public async Task Search_DeepMatchPreservesSelectionAndExpansion()
    {
        using var model = new TagManagementViewModel(new TagManagementTestService(), new TagDialogTestService());
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var team = model.Rows.Single(node => node.Id == "team");
        var platform = model.Rows.Single(node => node.Id == "platform");
        model.SelectedNode = platform;
        team.IsExpanded = false;
        model.SearchText = "API";

        Assert.Equal(new[] { "team", "platform", "api" }, model.Rows.Select(node => node.Id));
        Assert.False(team.IsExpanded);
        Assert.Same(platform, model.SelectedNode);
        model.SearchText = "missing tag";
        Assert.Empty(model.Rows);
        Assert.True(model.HasNoMatches);
        Assert.False(model.IsEmpty);
        Assert.Equal(3, model.BuiltInTags.Count);
        Assert.Equal("Team / Platform", model.SelectedPath);

        model.ClearSearchCommand.Execute(null);
        Assert.False(model.HasNoMatches);
        Assert.DoesNotContain(model.Rows, node => node.Id == "api");
        Assert.False(team.IsExpanded);
        Assert.Same(platform, model.SelectedNode);
    }

    /// <summary>空自定义数据保留环境，与无匹配搜索展示不同入口。</summary>
    [Fact]
    public async Task EmptyState_RetainsEnvironmentAndCanCreate()
    {
        var service = new TagManagementTestService();
        service.Tags.RemoveAll(tag => !tag.IsBuiltIn);
        using var model = new TagManagementViewModel(service, new TagDialogTestService());
        await model.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(model.IsEmpty);
        Assert.False(model.HasNoMatches);
        Assert.Equal(0, model.CustomTagCount);
        Assert.Equal(3, model.BuiltInTags.Count);
        Assert.True(model.NewTagCommand.CanExecute(null));
        model.SearchText = "production";
        Assert.False(model.IsEmpty);
        Assert.True(model.HasNoMatches);
        Assert.Equal(3, model.BuiltInTags.Count);
    }

    /// <summary>刷新使用稳定标识保留选择，详情切换到最新名称、路径、颜色和数量。</summary>
    [Fact]
    public async Task Reload_PreservesSelectionAndShowsLatestSnapshot()
    {
        var service = new TagManagementTestService();
        using var model = new TagManagementViewModel(service, new TagDialogTestService());
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        model.SelectedNode = model.Rows.Single(node => node.Id == "platform");
        var previous = model.SelectedNode;
        service.Tags.RemoveAll(tag => tag.Id == "platform");
        service.Tags.Add(new ManagedTag("platform", "Core Platform", "team", "#22C55E", false, "Team/Core Platform", 4));
        await model.ReloadAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("platform", model.SelectedNode?.Id);
        Assert.NotSame(previous, model.SelectedNode);
        Assert.Equal("Core Platform", model.SelectedTag?.Name);
        Assert.Equal("Team / Core Platform", model.SelectedPath);
        Assert.Equal("#22C55E", model.SelectedTag?.Color);
        Assert.Equal("4 个实例", model.SelectedInstanceLabel);
        Assert.True(model.SelectedNode?.IsSelected);

        service.Tags.RemoveAll(tag => tag.Id is "platform" or "api");
        await model.ReloadAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(model.HasSelection);
        Assert.False(model.EditCommand.CanExecute(null));
    }

    /// <summary>子级入口预填所选父级，成功创建后清空搜索并选择真实结果。</summary>
    [Fact]
    public async Task AddChild_UsesDialogBoundaryAndSelectsCreatedTag()
    {
        var service = new TagManagementTestService();
        var dialogs = new TagDialogTestService
        {
            EditOperation = async (_, parentId) => await service.SaveAsync(null, new TagEditDraft("Data", parentId, "#3B82F6"), TestContext.Current.CancellationToken),
        };
        using var model = new TagManagementViewModel(service, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        model.SelectedNode = model.Rows.Single(node => node.Id == "team");
        model.SearchText = "Team";
        await model.AddChildCommand.ExecuteAsync();

        Assert.Null(dialogs.LastEditTag);
        Assert.Equal("team", dialogs.LastParentId);
        Assert.Equal("", model.SearchText);
        Assert.Equal("created", model.SelectedTag?.Id);
        Assert.Equal("Team / Data", model.SelectedPath);
        Assert.Equal(7, model.CustomTagCount);
    }

    /// <summary>删除确认取消不修改状态，确认成功才重载并移除失效选择。</summary>
    [Fact]
    public async Task Delete_CancellationPreservesSelectionAndSuccessRemovesIt()
    {
        var service = new TagManagementTestService();
        var dialogs = new TagDialogTestService();
        using var model = new TagManagementViewModel(service, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        model.SelectedNode = model.Rows.Single(node => node.Id == "region");
        await model.DeleteCommand.ExecuteAsync();
        Assert.Equal("region", model.SelectedTag?.Id);
        Assert.Equal(0, service.DeleteCount);

        dialogs.DeleteOperation = async tag => { await service.DeleteAsync(tag.Id, TestContext.Current.CancellationToken); return true; };
        await model.DeleteCommand.ExecuteAsync();
        Assert.Equal("region", dialogs.LastDeleteTag?.Id);
        Assert.False(model.HasSelection);
        Assert.Equal(3, model.CustomTagCount);
        Assert.Equal(3, model.BuiltInTags.Count);
    }

    /// <summary>失败不是空态，重试会载入数据；释放订阅后数据源通知不再读取。</summary>
    [Fact]
    public async Task LoadFailure_RetryRecoversAndDisposeStopsEvents()
    {
        var service = new TagManagementTestService { ReadFailure = new IOException("数据源暂不可用") };
        using var model = new TagManagementViewModel(service, new TagDialogTestService());
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(model.IsInitialized);
        Assert.False(model.IsEmpty);
        Assert.Equal("数据源暂不可用", model.Error);

        service.ReadFailure = null;
        await model.RetryCommand.ExecuteAsync();
        Assert.True(model.IsInitialized);
        Assert.Equal("", model.Error);
        Assert.Equal(1, service.SubscriptionCount);
        model.Dispose();
        var reads = service.ReadCount;
        service.NotifyChanged();
        Assert.Equal(0, service.SubscriptionCount);
        Assert.Equal(reads, service.ReadCount);
    }

    /// <summary>关闭期间取消正在读取的快照，不把晚到结果写回已结束状态。</summary>
    [Fact]
    public async Task Dispose_CancelsPendingLoadWithoutPublishingRows()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ManagedTag>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new TagManagementTestService { NextRead = pending };
        using var model = new TagManagementViewModel(service, new TagDialogTestService());
        var loading = model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(model.IsLoading);
        model.Dispose();
        pending.TrySetResult(TagManagementTestData.Create());
        await loading;
        Assert.Empty(model.Rows);
        Assert.False(model.IsInitialized);
        Assert.Equal(0, service.SubscriptionCount);
    }
}
