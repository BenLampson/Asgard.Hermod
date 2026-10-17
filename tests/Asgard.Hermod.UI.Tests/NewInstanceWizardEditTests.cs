namespace Asgard.Hermod.UI.Tests;

/// <summary>编辑复用向导，但必须重新测试并更新原实例，取消不产生持久化写入。</summary>
public sealed class NewInstanceWizardEditTests
{
    /// <summary>预填配置不等于连接已验证，编辑只锁定顶层类型并拒绝该类型之外的服务商。</summary>
    [Fact]
    public async Task EditMode_PrefillsAndLocksIdentityWithoutTrustingSavedConnection()
    {
        var oss = new NewInstanceWizardTestProvider();
        var docker = new NewInstanceWizardTestDockerProvider();
        var configurations = new NewInstanceWizardTestConfigurationService();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(oss, docker),
            configurations, ExistingInstance(), ExistingConnection());
        Assert.True(model.IsEditing);
        Assert.Equal("编辑实例", model.WindowTitle);
        Assert.Equal("Existing OSS", model.Name);
        Assert.Equal("LTAIexisting0000", Field(model, "accessKeyId").Value);
        Assert.Equal("existing-secret", Field(model, "accessKeySecret").Value);
        Assert.Equal("storage", model.SelectedType?.Id);
        Assert.Same(oss.Descriptor, model.SelectedProvider);
        Assert.False(model.IsTypeSelectionEditable);
        Assert.True(model.IsProviderSelectionEditable);
        model.SelectedType = model.Types.Single(type => type.Id == "docker");
        model.SelectedProvider = docker.Descriptor;
        model.SelectedProvider = new NewInstanceWizardTestProvider("unregistered").Descriptor;
        model.SelectedType = null;
        model.SelectedProvider = null;
        Assert.Equal("storage", model.SelectedType?.Id);
        Assert.Same(oss.Descriptor, model.SelectedProvider);
        Assert.Equal("existing-secret", Field(model, "accessKeySecret").Value);
        model.NextCommand.Execute(null);
        model.NextCommand.Execute(null);
        Assert.Equal(3, model.CurrentStep);
        Assert.True(model.IsInformationComplete);
        Assert.False(model.HasSuccessfulConnectionTest);
        model.NextCommand.Execute(null);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Equal(3, model.CurrentStep);
        Assert.Equal(0, configurations.CallCount);
        Assert.Equal(0, configurations.UpdateCallCount);
    }

    /// <summary>切换云服务商会清空旧配置并作废测试，新测试通过后只更新原实例。</summary>
    [Fact]
    public async Task EditMode_ProviderChangeClearsCredentialsAndRequiresNewTestBeforeUpdatingOriginal()
    {
        var original = new NewInstanceWizardTestProvider();
        var selected = new NewInstanceWizardTestProvider("volcengine-tos", "Volcengine TOS");
        var configurations = new NewInstanceWizardTestConfigurationService();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(original, selected),
            configurations, ExistingInstance(), ExistingConnection());
        await ReachConfirmationAsync(model);
        var previousFields = model.Fields.ToArray();
        while (!model.IsProviderStep) model.PreviousCommand.Execute(null);

        model.SelectedProvider = selected.Descriptor;

        Assert.Equal("storage", model.SelectedType?.Id);
        Assert.Equal("Existing OSS", model.Name);
        Assert.Same(selected.Descriptor, model.SelectedProvider);
        Assert.All(previousFields, item => Assert.Empty(item.Value));
        Assert.All(model.Fields, item => Assert.Empty(item.Value));
        Assert.False(model.HasSuccessfulConnectionTest);
        Assert.False(model.CanCreate);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Empty(configurations.Updates);
        Field(model, "accessKeyId").Value = "VolcengineTestKey";
        Field(model, "accessKeySecret").Value = "new-provider-secret";
        await ReachConfirmationAsync(model);
        Assert.Equal(1, original.CallCount);
        Assert.Equal(1, selected.CallCount);
        Assert.Equal("volcengine-tos", selected.LastConnection?.ProviderId);
        Assert.Equal("new-provider-secret", selected.LastConnection?.Parameters["accessKeySecret"]);

        await model.SubmitCommand.ExecuteAsync();

        var update = Assert.Single(configurations.Updates);
        Assert.Equal("existing-instance", update.InstanceId);
        Assert.Equal("storage", update.Draft.Connection.TypeId);
        Assert.Equal("volcengine-tos", update.Draft.Connection.ProviderId);
        Assert.Equal("new-provider-secret", update.Draft.Connection.Parameters["accessKeySecret"]);
        Assert.Equal(0, configurations.CallCount);
        Assert.Equal("existing-instance", model.CreatedInstance?.Id);
        Assert.Equal("volcengine-tos", model.CreatedInstance?.ProviderId);
    }

    /// <summary>取消预填向导不会创建或更新配置，关闭结果与添加模式一致。</summary>
    [Fact]
    public async Task EditMode_CancelDoesNotSave()
    {
        var configurations = new NewInstanceWizardTestConfigurationService();
        using var model = CreateModel(configurations);
        var results = new List<bool>();
        model.CloseRequested += (_, accepted) => results.Add(accepted);
        model.NextCommand.Execute(null);
        Field(model, "accessKeySecret").Value = "modified-before-cancel";
        model.CancelCommand.Execute(null);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Equal(new[] { false }, results);
        Assert.Equal(0, configurations.CallCount);
        Assert.Equal(0, configurations.UpdateCallCount);
        Assert.Null(model.CreatedInstance);
    }

    /// <summary>字段更改必须重测，保存更新原 ID，并向宿主返回配置服务保留标签的快照。</summary>
    [Fact]
    public async Task EditMode_ChangedValueRequiresRetestAndReturnsOriginalIdentityAndTags()
    {
        var existing = ExistingInstance();
        var configurations = new NewInstanceWizardTestConfigurationService
        {
            // 标签标识在配置服务内，更新契约会保留关系，导航无需反向推导标签 ID。
            UpdateHandler = (id, draft, _) => Task.FromResult(existing with
            {
                Id = id,
                Name = draft.Name,
                DisplayName = draft.Name,
            }),
        };
        var provider = new NewInstanceWizardTestProvider();
        using var model = CreateModel(configurations, provider);
        await ReachConfirmationAsync(model);
        Assert.Equal("保存修改", model.ContinueLabel);
        Assert.True(model.CanCreate);
        Field(model, "accessKeySecret").Value = "updated-secret";
        model.Name = "Renamed OSS";
        Assert.False(model.CanCreate);
        Assert.False(model.HasSuccessfulConnectionTest);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Empty(configurations.Updates);
        model.PreviousCommand.Execute(null);
        model.PreviousCommand.Execute(null);
        await model.TestConnectionCommand.ExecuteAsync();
        model.NextCommand.Execute(null);
        model.NextCommand.Execute(null);
        var results = new List<bool>();
        model.CloseRequested += (_, accepted) => results.Add(accepted);
        await model.SubmitCommand.ExecuteAsync();
        var update = Assert.Single(configurations.Updates);
        Assert.Equal(existing.Id, update.InstanceId);
        Assert.Equal("storage", update.Draft.Connection.TypeId);
        Assert.Equal("aliyun-oss", update.Draft.Connection.ProviderId);
        Assert.Equal("updated-secret", update.Draft.Connection.Parameters["accessKeySecret"]);
        Assert.Equal("Renamed OSS", update.Draft.Name);
        Assert.Equal(0, configurations.CallCount);
        Assert.Equal(2, provider.CallCount);
        Assert.NotNull(model.CreatedInstance);
        Assert.Equal(existing.Id, model.CreatedInstance.Id);
        Assert.Equal(existing.Tags, model.CreatedInstance.Tags);
        Assert.Equal(new[] { true }, results);
    }

    /// <summary>更新进行中禁止重复提交及取消，成功只返回一次确认。</summary>
    [Fact]
    public async Task EditMode_PreventsDuplicateUpdate()
    {
        var completion = new TaskCompletionSource<InstanceSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var configurations = new NewInstanceWizardTestConfigurationService { UpdateHandler = (_, _, _) => completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken) };
        using var model = CreateModel(configurations);
        await ReachConfirmationAsync(model);
        var results = new List<bool>();
        model.CloseRequested += (_, accepted) => results.Add(accepted);
        Task updating = model.SubmitCommand.ExecuteAsync();
        Assert.True(model.IsSaving);
        Assert.Equal("保存中…", model.ContinueLabel);
        await model.SubmitCommand.ExecuteAsync();
        model.CancelCommand.Execute(null);
        Assert.Equal(1, configurations.UpdateCallCount);
        Assert.Equal(0, configurations.CallCount);
        Assert.Empty(results);
        completion.SetResult(ExistingInstance());
        await updating;
        await model.SubmitCommand.ExecuteAsync();
        Assert.Equal(1, configurations.UpdateCallCount);
        Assert.Equal(new[] { true }, results);
        Assert.Equal("existing-instance", model.CreatedInstance?.Id);
    }

    /// <summary>更新失败仅显示安全说明，填写内容与原实例仍可重试。</summary>
    [Fact]
    public async Task EditMode_UpdateFailureRetainsFieldsAndAllowsRetry()
    {
        var configurations = new NewInstanceWizardTestConfigurationService
        {
            UpdateHandler = (_, draft, _) => Task.FromException<InstanceSummary>(
                new InvalidOperationException(draft.Connection.Parameters["accessKeySecret"])),
        };
        using var model = CreateModel(configurations);
        await ReachConfirmationAsync(model);
        await model.SubmitCommand.ExecuteAsync();
        Assert.NotEmpty(model.SaveError);
        Assert.DoesNotContain("existing-secret", model.SaveError, StringComparison.Ordinal);
        Assert.Equal("existing-secret", Field(model, "accessKeySecret").Value);
        Assert.True(model.CanCreate);
        Assert.Null(model.CreatedInstance);
        configurations.UpdateHandler = (_, _, _) => Task.FromResult(ExistingInstance());
        await model.SubmitCommand.ExecuteAsync();
        Assert.Equal(2, configurations.UpdateCallCount);
        Assert.All(configurations.Updates, update => Assert.Equal("existing-instance", update.InstanceId));
        Assert.Equal(0, configurations.CallCount);
    }

    /// <summary>测试编辑入口使用的安全导航信息包含原标签展示。</summary>
    private static InstanceSummary ExistingInstance() => new("existing-instance", "Existing OSS", "Existing OSS",
        "cloud", ["生产", "备份"], IsLocal: true, TypeId: "storage", ProviderId: "aliyun-oss");

    /// <summary>凭据仅作为连接配置输入，不写入导航快照。</summary>
    private static InstanceConnectionOptions ExistingConnection() => new()
    {
        TypeId = "storage",
        ProviderId = "aliyun-oss",
        Parameters = new Dictionary<string, string>
        {
            ["accessKeyId"] = "LTAIexisting0000",
            ["accessKeySecret"] = "existing-secret",
        },
    };

    /// <summary>每个编辑用例都使用独立注册表与持久化边界。</summary>
    private static NewInstanceViewModel CreateModel(NewInstanceWizardTestConfigurationService configurations,
        NewInstanceWizardTestProvider? provider = null) => new(
            new NewInstanceWizardTestRegistry(provider ?? new()), configurations, ExistingInstance(), ExistingConnection());

    /// <summary>使用声明字段的稳定标识读写连接参数。</summary>
    private static InstanceConfigurationFieldViewModel Field(NewInstanceViewModel model, string key) =>
        NewInstanceWizardTests.GetField(model, key);

    /// <summary>保持预填值，按公开命令测试并到达确认更新页。</summary>
    private static async Task ReachConfirmationAsync(NewInstanceViewModel model)
    {
        model.NextCommand.Execute(null);
        model.NextCommand.Execute(null);
        Assert.Equal(3, model.CurrentStep);
        await model.TestConnectionCommand.ExecuteAsync();
        model.NextCommand.Execute(null);
        model.NextCommand.Execute(null);
        Assert.Equal(5, model.CurrentStep);
    }
}
