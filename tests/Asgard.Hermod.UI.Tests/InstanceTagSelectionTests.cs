namespace Asgard.Hermod.UI.Tests;

/// <summary>标签在所有类型的末步独立选择，不参与连接测试和协议字段。</summary>
public sealed class InstanceTagSelectionTests
{
    /// <summary>类型不同仍复用末步标签选择，前四步不能打开选择器。</summary>
    [Theory]
    [InlineData("storage")]
    [InlineData("ssh")]
    [InlineData("redis")]
    public async Task Tags_AreSelectableOnlyAtConfirmationForEveryType(string type)
    {
        var provider = new NewInstanceWizardTestProvider(type: new(type, type, type, "database"));
        var saves = new NewInstanceWizardTestConfigurationService();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(provider), saves,
            tags: TagManagementTestData.Create());
        Assert.Null(model.TagSelection.OpenPicker());
        model.SelectedType = Assert.Single(model.Types);
        Assert.Null(model.TagSelection.OpenPicker());
        model.NextCommand.Execute(null);
        model.Name = "Tagged instance";
        NewInstanceWizardTests.GetField(model, "accessKeyId").Value = "TestAccessKey";
        NewInstanceWizardTests.GetField(model, "accessKeySecret").Value = "secret";
        Assert.Null(model.TagSelection.OpenPicker());
        model.NextCommand.Execute(null);
        Assert.Null(model.TagSelection.OpenPicker());
        await model.TestConnectionCommand.ExecuteAsync();
        model.NextCommand.Execute(null);
        Assert.Null(model.TagSelection.OpenPicker());
        model.NextCommand.Execute(null);
        var picker = Assert.IsType<InstanceTagPickerViewModel>(model.TagSelection.OpenPicker());
        Assert.False(model.CanCreate);
        picker.Rows.Single(row => row.Tag.Id == "platform").IsSelected = true;
        picker.ApplyCommand.Execute(null);
        Assert.Equal(["platform"], model.TagSelection.SelectedIds);
        Assert.True(model.HasSuccessfulConnectionTest);
        model.PreviousCommand.Execute(null);
        Assert.False(model.TagSelection.IsEditable);
        model.NextCommand.Execute(null);
        Assert.Equal(["platform"], model.TagSelection.SelectedIds);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Equal(["platform"], Assert.Single(saves.Drafts).TagIds);
        Assert.True(Assert.Single(saves.Drafts).ReplaceTags);
    }

    /// <summary>父子标签都能独立选择，搜索、折叠与取消不改变已经确认的选择。</summary>
    [Fact]
    public async Task Picker_PreservesIndependentSelectionAcrossSearchAndCancellation()
    {
        using var model = Create();
        await NewInstanceWizardTests.ReachConfirmationAsync(model);
        var picker = Assert.IsType<InstanceTagPickerViewModel>(model.TagSelection.OpenPicker());
        picker.Rows.Single(row => row.Tag.Id == "team").IsSelected = true;
        Assert.Equal(["team"], picker.SelectedIds);
        picker.SearchText = "Platform";
        picker.Rows.Single(row => row.Tag.Id == "api").IsSelected = true;
        picker.SearchText = "no matches";
        Assert.Empty(picker.Rows);
        Assert.Equal(["team", "api"], picker.SelectedIds);
        picker.ApplyCommand.Execute(null);
        Assert.Equal(["team", "api"], model.TagSelection.SelectedIds);

        var cancelled = Assert.IsType<InstanceTagPickerViewModel>(model.TagSelection.OpenPicker());
        cancelled.ClearCommand.Execute(null);
        cancelled.CancelCommand.Execute(null);
        Assert.Equal(["team", "api"], model.TagSelection.SelectedIds);
        var cleared = Assert.IsType<InstanceTagPickerViewModel>(model.TagSelection.OpenPicker());
        cleared.ClearCommand.Execute(null);
        cleared.ApplyCommand.Execute(null);
        Assert.Empty(model.TagSelection.SelectedIds);
        Assert.True(model.CanCreate);
    }

    /// <summary>保存失败保留已选标签，保存中禁止重复打开或改变选择。</summary>
    [Fact]
    public async Task Saving_FreezesTagsAndRetainsSelectionOnFailure()
    {
        var completion = new TaskCompletionSource<InstanceSummary>();
        var saves = new NewInstanceWizardTestConfigurationService { CreateHandler = (_, _) => completion.Task };
        using var model = Create(saves);
        await NewInstanceWizardTests.ReachConfirmationAsync(model);
        var picker = Assert.IsType<InstanceTagPickerViewModel>(model.TagSelection.OpenPicker());
        picker.Rows.Single(row => row.Tag.Id == "eu").IsSelected = true;
        picker.ApplyCommand.Execute(null);
        var saving = model.SubmitCommand.ExecuteAsync();
        Assert.True(model.IsSaving);
        Assert.Null(model.TagSelection.OpenPicker());
        model.TagSelection.RemoveCommand.Execute("eu");
        Assert.Equal(["eu"], model.TagSelection.SelectedIds);
        completion.SetException(new IOException("secret response must stay hidden"));
        await saving;
        Assert.Equal(["eu"], model.TagSelection.SelectedIds);
        Assert.True(model.CanCreate);
        Assert.DoesNotContain("secret response", model.SaveError);
    }

    /// <summary>编辑原关联可在末步调整，恢复由服务读取的标识，不依赖路径匹配。</summary>
    [Fact]
    public async Task Editing_RestoresAndReplacesStableTagIds()
    {
        var provider = new NewInstanceWizardTestProvider();
        var saves = new NewInstanceWizardTestConfigurationService();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(provider), saves,
            new InstanceSummary("id", "Original", "Original", "cloud", ["old/renamed/path"], TypeId: "storage", ProviderId: "aliyun-oss"),
            new InstanceConnectionOptions { TypeId = "storage", ProviderId = "aliyun-oss", Parameters = new Dictionary<string, string>() },
            tags: TagManagementTestData.Create(), selectedTagIds: ["platform"]);
        Assert.Equal(["platform"], model.TagSelection.SelectedIds);
        await NewInstanceWizardTests.ReachConfirmationAsync(model);
        var picker = Assert.IsType<InstanceTagPickerViewModel>(model.TagSelection.OpenPicker());
        picker.ClearCommand.Execute(null);
        picker.Rows.Single(row => row.Tag.Id == "eu").IsSelected = true;
        picker.ApplyCommand.Execute(null);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Equal(["eu"], Assert.Single(saves.Updates).Draft.TagIds);
        Assert.True(Assert.Single(saves.Updates).Draft.ReplaceTags);
    }

    /// <summary>每个用例隔离目录与保存边界，不依赖真实用户语言和标签数据。</summary>
    private static NewInstanceViewModel Create(NewInstanceWizardTestConfigurationService? saves = null) =>
        new(new NewInstanceWizardTestRegistry(new NewInstanceWizardTestProvider()), saves ?? new(), tags: TagManagementTestData.Create());
}
