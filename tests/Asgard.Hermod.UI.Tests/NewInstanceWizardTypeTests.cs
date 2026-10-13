namespace Asgard.Hermod.UI.Tests;

/// <summary>验证顶层类型隔离、服务发现和不同连接器字段，Docker 仅作为测试替身。</summary>
public sealed class NewInstanceWizardTypeTests
{
    /// <summary>先选择顶层类型，再展示其服务；未选择时不存在默认存储假设。</summary>
    [Fact]
    public void TypeSelection_FiltersProvidersAndClearsPreviousFields()
    {
        var oss = new NewInstanceWizardTestProvider();
        var docker = new NewInstanceWizardTestDockerProvider();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(oss, docker),
            new NewInstanceWizardTestConfigurationService());
        Assert.Equal(new[] { "storage", "docker" }, model.Types.Select(type => type.Id));
        Assert.Null(model.SelectedType);
        Assert.True(model.IsChoosingType);
        Assert.Empty(model.Steps);
        Assert.Empty(model.StepCountText);
        Assert.False(model.HasPreviousStep);
        Assert.Empty(model.Providers);
        Assert.Empty(model.Fields);
        Assert.False(model.NextCommand.CanExecute(null));

        model.SelectedType = model.Types.Single(type => type.Id == "storage");
        Assert.False(model.IsChoosingType);
        Assert.Equal(5, model.Steps.Count);
        Assert.True(model.HasPreviousStep);
        Assert.Same(oss.Descriptor, Assert.Single(model.Providers));
        Assert.Same(oss.Descriptor, model.SelectedProvider);
        Assert.Equal(new[] { "accessKeyId", "accessKeySecret" }, model.Fields.Select(field => field.Key));
        var previousKey = NewInstanceWizardTests.GetField(model, "accessKeyId");
        var previousSecret = NewInstanceWizardTests.GetField(model, "accessKeySecret");
        previousKey.Value = "LTAIprevious";
        previousSecret.Value = "previous-secret";
        model.SelectedProvider = docker.Descriptor;
        Assert.Same(oss.Descriptor, model.SelectedProvider);

        model.SelectedType = model.Types.Single(type => type.Id == "docker");
        Assert.Same(docker.Descriptor, Assert.Single(model.Providers));
        Assert.Same(docker.Descriptor, model.SelectedProvider);
        Assert.Equal(new[] { "endpoint", "apiToken" }, model.Fields.Select(field => field.Key));
        Assert.All(model.Fields, field => Assert.Empty(field.Value));
        Assert.Empty(previousKey.Value);
        Assert.Empty(previousSecret.Value);
        Assert.True(model.HasProviderSelection);
    }

    /// <summary>返回公共入口会清空旧连接，重新选择任何类型都从它的首步开始。</summary>
    [Fact]
    public async Task BackFromFirstStep_ReturnsToTypePickerAndClearsVerifiedConnection()
    {
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new NewInstanceWizardTestProvider(),
            new NewInstanceWizardTestDockerProvider()), new NewInstanceWizardTestConfigurationService());
        await NewInstanceWizardTests.ReachConfirmationAsync(model);
        var oldFields = model.Fields.ToArray();
        while (model.CurrentStep > 1)
            model.PreviousCommand.Execute(null);
        model.PreviousCommand.Execute(null);
        Assert.True(model.IsChoosingType);
        Assert.Null(model.SelectedType);
        Assert.Null(model.SelectedProvider);
        Assert.Empty(model.Providers);
        Assert.Empty(model.Steps);
        Assert.Empty(model.StepCountText);
        Assert.Empty(model.Name);
        Assert.Empty(model.Fields);
        Assert.All(oldFields, item => Assert.Empty(item.Value));
        Assert.False(model.HasSuccessfulConnectionTest);
        Assert.False(model.CanGoNext);
        Assert.False(model.CanGoPrevious);
        model.SelectedType = model.Types.Single(type => type.Id == "docker");
        Assert.Equal(1, model.CurrentStep);
        Assert.Equal(5, model.Steps.Count);
        Assert.All(model.Fields, item => Assert.Empty(item.Value));
    }

    /// <summary>多个服务商需要明确选择，跨类型或未知服务不能绕过类型限制。</summary>
    [Fact]
    public void ProviderSelection_RequiresProviderFromSelectedType()
    {
        var first = new NewInstanceWizardTestProvider();
        var second = new NewInstanceWizardTestProvider("other-oss", "其他存储");
        var docker = new NewInstanceWizardTestDockerProvider();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(first, second, docker),
            new NewInstanceWizardTestConfigurationService());
        model.SelectedType = model.Types.Single(type => type.Id == "storage");
        Assert.Equal(2, model.Providers.Count);
        Assert.Null(model.SelectedProvider);
        Assert.False(model.CanGoNext);
        model.SelectedProvider = docker.Descriptor;
        Assert.Null(model.SelectedProvider);
        model.SelectedProvider = second.Descriptor;
        Assert.True(model.CanGoNext);
        model.SelectedType = new("unregistered", "未知", "", "cloud");
        Assert.Equal("storage", model.SelectedType?.Id);
    }

    /// <summary>普通地址和可选密钥由声明字段驱动，并携带正确类型传给测试与保存服务。</summary>
    [Fact]
    public async Task DockerFields_TestAndCreateUsingDeclaredTypeAndParameters()
    {
        var oss = new NewInstanceWizardTestProvider();
        var docker = new NewInstanceWizardTestDockerProvider();
        var configurations = new NewInstanceWizardTestConfigurationService();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(oss, docker), configurations);
        model.SelectedType = model.Types.Single(type => type.Id == "docker");
        model.NextCommand.Execute(null);
        model.Name = "  Docker development  ";
        var endpoint = NewInstanceWizardTests.GetField(model, "endpoint");
        var token = NewInstanceWizardTests.GetField(model, "apiToken");
        endpoint.Value = "  http://localhost:2375  ";
        Assert.True(token.IsValid);
        Assert.False(token.IsRequired);
        Assert.True(token.IsSecret);
        token.Value = "private-docker-token";
        Assert.Equal("http://localhost:2375", endpoint.ConfirmationValue);
        Assert.Equal("已填写 · 隐藏", token.ConfirmationValue);
        model.NextCommand.Execute(null);
        await model.TestConnectionCommand.ExecuteAsync();
        Assert.Equal(0, oss.CallCount);
        Assert.Equal(1, docker.CallCount);
        Assert.NotNull(docker.LastConnection);
        Assert.Equal("docker", docker.LastConnection.TypeId);
        Assert.Equal("docker-engine", docker.LastConnection.ProviderId);
        Assert.Equal("Docker / Docker Engine", model.ProviderSummary);
        model.NextCommand.Execute(null);
        model.NextCommand.Execute(null);
        await model.SubmitCommand.ExecuteAsync();
        var draft = Assert.Single(configurations.Drafts);
        Assert.Equal("docker", draft.Connection.TypeId);
        Assert.Equal("docker-engine", draft.Connection.ProviderId);
        Assert.Equal("Docker development", draft.Name);
        Assert.Equal("http://localhost:2375", draft.Connection.Parameters["endpoint"]);
        Assert.Equal("private-docker-token", draft.Connection.Parameters["apiToken"]);
        Assert.DoesNotContain("accessKeyId", draft.Connection.Parameters.Keys);
    }

    /// <summary>更改类型后，即使旧连接已成功，也不能继续使用原测试权限。</summary>
    [Fact]
    public async Task TypeChange_InvalidatesSuccessfulConnectionTest()
    {
        var oss = new NewInstanceWizardTestProvider();
        var docker = new NewInstanceWizardTestDockerProvider();
        var configurations = new NewInstanceWizardTestConfigurationService();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(oss, docker), configurations);
        await NewInstanceWizardTests.ReachConfirmationAsync(model);
        Assert.True(model.CanCreate);
        model.SelectedType = model.Types.Single(type => type.Id == "docker");
        Assert.False(model.HasSuccessfulConnectionTest);
        Assert.False(model.CanCreate);
        Assert.Empty(model.TestChecks);
        Assert.Equal(InstanceConnectionTestState.NotStarted, model.TestState);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Empty(configurations.Drafts);
    }

    /// <summary>切换类型传递取消令牌，忽略旧连接器未遵守取消的迟到结果。</summary>
    [Fact]
    public async Task TypeChangeDuringTest_CancelsAndIgnoresPreviousTypeSuccess()
    {
        var completion = new TaskCompletionSource<InstanceConnectionTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oss = new NewInstanceWizardTestProvider { TestHandler = (_, _) => completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken) };
        var docker = new NewInstanceWizardTestDockerProvider();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(oss, docker),
            new NewInstanceWizardTestConfigurationService());
        NewInstanceWizardTests.MoveToTest(model);
        Task running = model.TestConnectionCommand.ExecuteAsync();
        model.SelectedType = model.Types.Single(type => type.Id == "docker");
        Assert.True(oss.LastCancellationToken.IsCancellationRequested);
        completion.SetResult(new(true, "旧类型成功"));
        await running;
        Assert.False(model.HasSuccessfulConnectionTest);
        Assert.Equal(1, model.CurrentStep);
        Assert.False(model.CanTest);
        Assert.Equal(InstanceConnectionTestState.NotStarted, model.TestState);
        Assert.All(model.Fields, field => Assert.Empty(field.Value));
    }
}
