using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证用户可见的颜色、父级规则和失败恢复，不模拟内部计算步骤。</summary>
public sealed class TagEditorViewModelTests
{
    /// <summary>大小写和短 HEX 均提交六位大写颜色，预览即时更新。</summary>
    [Theory]
    [InlineData("#3aF", "#33AAFF")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("#FFFFFF", "#FFFFFF")]
    [InlineData("  #1a2b3c  ", "#1A2B3C")]
    public async Task Color_ValidHexNormalizesPreviewAndSavedDraft(string input, string expected)
    {
        var service = new TagManagementTestService();
        using var model = new TagEditorViewModel(service) { Name = "Data", ColorText = input };
        await model.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(model.CanSave);
        Assert.Equal(expected, model.PreviewColor);
        Assert.Equal("", model.ColorError);
        await model.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, service.LastDraft?.Color);
        Assert.Equal(input, model.ColorText);
    }

    /// <summary>非法输入保留上次有效颜色、名称与父级，保存入口被禁用。</summary>
    [Theory]
    [InlineData("#12")]
    [InlineData("123456")]
    [InlineData("#GGG")]
    [InlineData("#00112233")]
    [InlineData("")]
    public async Task Color_InvalidHexPreservesLastPreviewAndBlocksPersistence(string invalid)
    {
        var service = new TagManagementTestService();
        using var model = new TagEditorViewModel(service, parentId: "team") { Name = "Data", ColorText = "#3aF" };
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        model.ColorText = invalid;

        Assert.Equal("#33AAFF", model.PreviewColor);
        Assert.Equal("Team / Data", model.PreviewPath);
        Assert.NotEmpty(model.ColorError);
        Assert.False(model.CanSave);
        Assert.False(model.SaveCommand.CanExecute(null));
        await model.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, service.SaveCount);

        model.SelectColorCommand.Execute("#8B5CF6");
        Assert.True(model.CanSave);
        Assert.True(Assert.Single(model.Swatches, swatch => swatch.Color == "#8B5CF6").IsSelected);
    }

    /// <summary>禁止把标签移动到自身、任意后代或内置环境，根级与其他自定义仍可选。</summary>
    [Fact]
    public async Task ParentOptions_ExcludeSelfDescendantsAndBuiltInEnvironment()
    {
        var service = new TagManagementTestService();
        var team = service.Tags.Single(tag => tag.Id == "team");
        using var model = new TagEditorViewModel(service, team);
        await model.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new string?[] { null, "region", "eu", "us" }, model.ParentOptions.Select(option => option.Id));
        Assert.Equal("根级", model.SelectedParent?.DisplayName);
        model.ParentId = "api";
        Assert.NotEmpty(model.ParentError);
        Assert.False(model.CanSave);
        await model.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, service.SaveCount);

        model.SelectedParent = model.ParentOptions.Single(option => option.Id == "eu");
        Assert.True(model.CanSave);
        Assert.Equal("Region / EU West / Team", model.PreviewPath);
    }

    /// <summary>同级重名由服务检查，失败不会关闭表单或丢失修改，修正后可重试。</summary>
    [Fact]
    public async Task Save_ServiceFailureKeepsDraftAndAllowsRetry()
    {
        var service = new TagManagementTestService { SaveFailure = new InvalidOperationException("同一父级下已存在此名称。") };
        using var model = new TagEditorViewModel(service, service.Tags.Single(tag => tag.Id == "platform"));
        var completions = new List<bool>();
        model.CloseRequested += (_, accepted) => completions.Add(accepted);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        model.Name = "  Core Platform  ";
        model.ColorText = "#3af";
        await model.SaveCommand.ExecuteAsync();

        Assert.Empty(completions);
        Assert.Null(model.Result);
        Assert.Equal(Text.Get("TheOperationFailedTryAgain"), model.Error);
        Assert.Equal("  Core Platform  ", model.Name);
        Assert.Equal("team", model.ParentId);
        Assert.Equal("#3af", model.ColorText);
        Assert.True(model.CanSave);

        service.SaveFailure = null;
        await model.SaveCommand.ExecuteAsync();
        Assert.Equal(new[] { true }, completions);
        Assert.Equal("platform", model.Result?.Id);
        Assert.Equal("platform", service.LastSavedId);
        Assert.Equal(new TagEditDraft("Core Platform", "team", "#33AAFF"), service.LastDraft);
    }

    /// <summary>首次读取失败保留预填草稿，重试只载入候选，不重置输入。</summary>
    [Fact]
    public async Task Initialize_FailureCanRetryWithoutLosingChildDraft()
    {
        var service = new TagManagementTestService { ReadFailure = new IOException("读取失败") };
        using var model = new TagEditorViewModel(service, parentId: "team") { Name = "Data", ColorText = "#abc" };
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(model.IsInitialized);
        Assert.False(model.CanSave);
        Assert.Equal(Text.Get("TheOperationFailedTryAgain"), model.Error);

        service.ReadFailure = null;
        await model.RetryCommand.ExecuteAsync();
        Assert.True(model.CanSave);
        Assert.Equal("Data", model.Name);
        Assert.Equal("team", model.ParentId);
        Assert.Equal("#abc", model.ColorText);
        Assert.Equal("Team / Data", model.PreviewPath);
    }

    /// <summary>空白名称与系统环境均不能绕过界面入口写入。</summary>
    [Fact]
    public async Task Save_RejectsEmptyNameAndBuiltInTags()
    {
        var service = new TagManagementTestService();
        using var empty = new TagEditorViewModel(service) { Name = "  " };
        using var builtIn = new TagEditorViewModel(service, service.Tags.Single(tag => tag.Id == "production"));
        await empty.InitializeAsync(TestContext.Current.CancellationToken);
        await builtIn.InitializeAsync(TestContext.Current.CancellationToken);
        await empty.SaveAsync(TestContext.Current.CancellationToken);
        await builtIn.SaveAsync(TestContext.Current.CancellationToken);
        Assert.False(empty.CanSave);
        Assert.False(builtIn.CanSave);
        Assert.NotEmpty(empty.NameError);
        Assert.Equal(0, service.SaveCount);
    }

    /// <summary>名称的路径分隔和控制字符规则在调用后端前直接反馈。</summary>
    [Theory]
    [InlineData("Team/Data")]
    [InlineData("Data\tAPI")]
    [InlineData("Data\nAPI")]
    public async Task Name_PathOrControlCharactersBlockSave(string name)
    {
        var service = new TagManagementTestService();
        using var model = new TagEditorViewModel(service) { Name = name };
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(model.NameError);
        Assert.False(model.CanSave);
        await model.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, service.SaveCount);
    }

    /// <summary>八十字符边界与后端一致，不允许长名称延后才报错。</summary>
    [Fact]
    public async Task Name_LengthLimitIsCheckedBeforePersistence()
    {
        var service = new TagManagementTestService();
        using var model = new TagEditorViewModel(service) { Name = new string('A', 81) };
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(model.CanSave);
        model.Name = new string('A', 80);
        Assert.True(model.CanSave);
    }
}
