namespace Asgard.Hermod.UI.Tests;

/// <summary>验证标签过滤对真实可见实例的影响，包括父路径边界和名称查询。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class TagFilterTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>CollectionView 的创建和刷新都在共享 STA 中执行。</summary>
    public TagFilterTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>父路径覆盖自身和子级，斜杠边界排除名字近似的相邻分支。</summary>
    [Fact]
    public Task ParentPath_MatchesExactAndNestedTagsButNotSimilarPrefix() => _fixture.RunAsync(() =>
    {
        var sidebar = TagPickerTestData.CreateSidebar();
        sidebar.ApplyTagFilter([" environment // production / "], TagFilterMatchMode.Any);

        Assert.Equal(new[] { "api-east", "api-west", "root" }, TagPickerTestData.VisibleIds(sidebar));
        Assert.DoesNotContain(sidebar.Instances.Cast<InstanceItemViewModel>(), item => item.Id == "copy");

        sidebar.Query = "API";
        Assert.Equal(new[] { "api-east", "api-west" }, TagPickerTestData.VisibleIds(sidebar));
        sidebar.Query = "Worker";
        Assert.True(sidebar.IsEmpty);
        sidebar.ClearTagCommand.Execute(null);
        Assert.Equal(new[] { "worker" }, TagPickerTestData.VisibleIds(sidebar));
        Assert.Equal("Worker", sidebar.Query);
        return Task.CompletedTask;
    });

    /// <summary>任意和全部匹配在多分组标签之间产生不同结果，不把同枝的子级当作独立要求。</summary>
    [Theory]
    [InlineData(TagFilterMatchMode.Any)]
    [InlineData(TagFilterMatchMode.All)]
    public Task MultipleGroups_AnyAndAllMatchAppliedPaths(TagFilterMatchMode mode) => _fixture.RunAsync(() =>
    {
        var sidebar = TagPickerTestData.CreateSidebar();
        sidebar.ApplyTagFilter(["Environment/Production", "Owner/Platform"], mode);

        string[] expected = mode == TagFilterMatchMode.Any
            ? ["api-east", "api-west", "copy", "root", "worker"] : ["api-east"];
        Assert.Equal(expected, TagPickerTestData.VisibleIds(sidebar));
        Assert.Equal(mode, sidebar.TagMatchMode);
        Assert.Equal(new[] { "Environment/Production", "Owner/Platform" }, sidebar.SelectedTagPaths);

        sidebar.ApplyTagFilter([], mode);
        Assert.Equal(6, sidebar.Instances.Cast<InstanceItemViewModel>().Count());
        Assert.False(sidebar.IsEmpty);
        return Task.CompletedTask;
    });

    /// <summary>选择器树来自实际实例标签，相同父路径只出现一次且数量涵盖多层后代。</summary>
    [Fact]
    public Task AvailableTree_ComesFromInstancesAndCountsNestedDescendants() => _fixture.RunAsync(() =>
    {
        var sidebar = TagPickerTestData.CreateSidebar();
        var roots = sidebar.BuildTagFilterDefinitions();
        var environment = Assert.Single(roots, node => node.Path == "Environment");
        var production = Assert.Single(environment.Children, node => node.Path == "Environment/Production");
        var owner = Assert.Single(roots, node => node.Path == "Owner");

        Assert.Equal(2, roots.Count);
        Assert.Equal(5, environment.Count);
        Assert.Equal(2, production.Count);
        Assert.Equal(new[] { "cn-east", "eu-west" }, production.Children.Select(node => node.Name));
        Assert.Equal(2, owner.Count);
        Assert.Equal(3, environment.Children.Count);
        return Task.CompletedTask;
    });
}
