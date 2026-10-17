namespace Asgard.Hermod.UI.Tests;

/// <summary>验证标签草稿的嵌套选择与搜索，断言公开结果而不是内部计算步骤。</summary>
public sealed class TagTreePickerViewModelTests
{
    /// <summary>叶子改变祖先半选，整枝选择压缩为父路径，清除释放全部选择。</summary>
    [Fact]
    public void Selection_CascadesAndKeepsAncestorHalfCheckedState()
    {
        var model = new TagTreePickerViewModel(TagPickerTestData.Definitions(), [], TagFilterMatchMode.Any);
        var environment = TagPickerTestData.Node(model, "Environment");
        var production = TagPickerTestData.Node(model, "Environment/Production");
        var east = TagPickerTestData.Node(model, "Environment/Production/cn-east");
        var west = TagPickerTestData.Node(model, "Environment/Production/eu-west");
        var staging = TagPickerTestData.Node(model, "Environment/Staging");

        east.IsChecked = true;
        Assert.Null(production.IsChecked);
        Assert.Null(environment.IsChecked);
        Assert.Equal<bool?>(false, west.IsChecked);
        Assert.Equal(new[] { east.Path }, model.SelectedPaths);

        west.IsChecked = true;
        Assert.Equal<bool?>(true, production.IsChecked);
        Assert.Null(environment.IsChecked);
        Assert.Equal(new[] { production.Path }, model.SelectedPaths);

        environment.IsChecked = true;
        Assert.All(TagPickerTestData.Descendants([environment]), node => Assert.Equal<bool?>(true, node.IsChecked));
        Assert.Equal(new[] { environment.Path }, model.SelectedPaths);

        west.IsChecked = false;
        Assert.Null(production.IsChecked);
        Assert.Null(environment.IsChecked);
        Assert.Equal<bool?>(true, staging.IsChecked);
        Assert.Equal(new[] { east.Path, staging.Path }, model.SelectedPaths);

        model.ClearCommand.Execute(null);
        Assert.Empty(model.SelectedPaths);
        Assert.All(TagPickerTestData.Descendants(model.Roots), node => Assert.Equal<bool?>(false, node.IsChecked));
    }

    /// <summary>已存的父路径重建完整分支，移除一个选择标签不影响另一棵树。</summary>
    [Fact]
    public void InitialSelection_ExpandsSavedParentAndRemovingChipKeepsOtherBranch()
    {
        var model = new TagTreePickerViewModel(TagPickerTestData.Definitions(),
            ["Environment", "Owner/Platform"], TagFilterMatchMode.All);
        var environment = TagPickerTestData.Node(model, "Environment");
        var owner = TagPickerTestData.Node(model, "Owner");
        var platform = TagPickerTestData.Node(model, "Owner/Platform");

        Assert.All(TagPickerTestData.Descendants([environment]), node => Assert.Equal<bool?>(true, node.IsChecked));
        Assert.Null(owner.IsChecked);
        Assert.Equal<bool?>(true, platform.IsChecked);
        Assert.Equal(new[] { "Environment", "Owner/Platform" }, model.SelectedPaths);

        model.RemoveSelectedCommand.Execute("Environment");

        Assert.All(TagPickerTestData.Descendants([environment]), node => Assert.Equal<bool?>(false, node.IsChecked));
        Assert.Equal<bool?>(true, platform.IsChecked);
        Assert.Null(owner.IsChecked);
        Assert.Equal(new[] { platform.Path }, model.SelectedPaths);
        Assert.Equal(TagFilterMatchMode.All, model.MatchMode);
    }

    /// <summary>深层命中保留上下文并展开祖先，父路径检索包含子级且不清除隐藏选择。</summary>
    [Fact]
    public void Search_DeepMatchKeepsAncestorsAndDoesNotChangeDraftSelection()
    {
        var model = new TagTreePickerViewModel(TagPickerTestData.Definitions(),
            ["Environment/Staging"], TagFilterMatchMode.Any);
        var environment = TagPickerTestData.Node(model, "Environment");
        var production = TagPickerTestData.Node(model, "Environment/Production");
        environment.IsExpanded = false;
        production.IsExpanded = false;

        model.SearchText = "eu-west";

        Assert.Equal(new[] { "Environment", "Environment/Production", "Environment/Production/eu-west" },
            model.Rows.Select(node => node.Path));
        Assert.True(environment.IsEffectivelyExpanded);
        Assert.True(production.IsEffectivelyExpanded);
        Assert.False(environment.IsExpanded);
        Assert.False(production.IsExpanded);
        Assert.Equal(new[] { "Environment/Staging" }, model.SelectedPaths);

        model.SearchText = "production";
        Assert.Equal(new[]
        {
            "Environment", "Environment/Production", "Environment/Production/cn-east", "Environment/Production/eu-west"
        }, model.Rows.Select(node => node.Path));

        model.SearchText = "missing tag";
        Assert.Empty(model.Rows);
        Assert.Equal(new[] { "Environment/Staging" }, model.SelectedPaths);

        model.SearchText = "";
        Assert.False(environment.IsEffectivelyExpanded);
        Assert.False(production.IsEffectivelyExpanded);
        Assert.DoesNotContain(model.Rows, node => node.Path == "Environment/Staging");
        environment.IsExpanded = true;
        Assert.Contains(model.Rows, node => node.Path == "Environment/Staging");
        Assert.Equal<bool?>(true, TagPickerTestData.Node(model, "Environment/Staging").IsChecked);
    }

    /// <summary>取消恢复打开前的标签和模式，并提供明确的不应用结果。</summary>
    [Fact]
    public void Cancel_RestoresOpeningSelectionAndMatchMode()
    {
        var model = new TagTreePickerViewModel(TagPickerTestData.Definitions(),
            ["Environment/Production"], TagFilterMatchMode.All);
        var completions = new List<bool>();
        model.Completed += (_, applied) => completions.Add(applied);
        model.ClearCommand.Execute(null);
        TagPickerTestData.Node(model, "Owner").IsChecked = true;
        model.MatchMode = TagFilterMatchMode.Any;

        model.CancelCommand.Execute(null);

        Assert.Equal(new[] { false }, completions);
        Assert.Equal(new[] { "Environment/Production" }, model.SelectedPaths);
        Assert.Equal(TagFilterMatchMode.All, model.MatchMode);
        Assert.Equal<bool?>(false, TagPickerTestData.Node(model, "Owner").IsChecked);
        Assert.Equal<bool?>(true, TagPickerTestData.Node(model, "Environment/Production").IsChecked);
    }
}
