namespace Asgard.Hermod.UI.Tests;

/// <summary>验证五步向导的业务门禁、真实服务边界、取消和失败恢复。</summary>
public sealed class NewInstanceWizardTests
{
    /// <summary>未填写信息时保持第二步，并分别提示三个必填字段。</summary>
    [Fact]
    public void NextCommand_ValidatesRequiredFieldsInPlace()
    {
        using var model = CreateModel();
        model.NextCommand.Execute(null);
        model.NextCommand.Execute(null);
        Assert.Equal(2, model.CurrentStep);
        Assert.NotEmpty(model.NameError);
        Assert.NotEmpty(GetField(model, "accessKeyId").Error);
        Assert.NotEmpty(GetField(model, "accessKeySecret").Error);

        model.Name = "测试 OSS";
        GetField(model, "accessKeyId").Value = "LTAITEST000000000000";
        Assert.Empty(model.NameError);
        Assert.Empty(GetField(model, "accessKeyId").Error);
        Assert.NotEmpty(GetField(model, "accessKeySecret").Error);
        GetField(model, "accessKeySecret").Value = "  ";
        model.NextCommand.Execute(null);
        Assert.Equal(2, model.CurrentStep);
    }

    /// <summary>服务层限制在填写页就地提示，不等到提交时才出现泛化保存错误。</summary>
    [Theory]
    [InlineData("NameTooLong")]
    [InlineData("NameControl")]
    [InlineData("KeyTooLong")]
    [InlineData("KeyFormat")]
    [InlineData("SecretTooLong")]
    [InlineData("SecretControl")]
    public void NextCommand_RejectsInvalidInformationBeforeConnectionTest(string invalidField)
    {
        using var model = CreateModel();
        model.NextCommand.Execute(null);
        model.Name = "OSS regression";
        GetField(model, "accessKeyId").Value = "LTAIREGRESSION000000";
        GetField(model, "accessKeySecret").Value = "regression-secret";
        switch (invalidField)
        {
            case "NameTooLong": model.Name = new string('名', 129); break;
            case "NameControl": model.Name = "OSS\u0001regression"; break;
            case "KeyTooLong": GetField(model, "accessKeyId").Value = new string('A', 129); break;
            case "KeyFormat": GetField(model, "accessKeyId").Value = "LTAI-not-valid"; break;
            case "SecretTooLong": GetField(model, "accessKeySecret").Value = new string('S', 257); break;
            default: GetField(model, "accessKeySecret").Value = "secret\u0001value"; break;
        }
        model.NextCommand.Execute(null);
        Assert.Equal(2, model.CurrentStep);
        Assert.False(model.IsInformationComplete);
        Assert.False(model.TestConnectionCommand.CanExecute(null));
        Assert.Contains(new[] { model.NameError, GetField(model, "accessKeyId").Error, GetField(model, "accessKeySecret").Error },
            error => !string.IsNullOrEmpty(error));
    }

    /// <summary>即使直接执行提交命令，也不能跳过测试或从未完成的步骤保存。</summary>
    [Fact]
    public async Task SubmitCommand_CannotBypassSuccessfulConnectionTest()
    {
        var provider = new NewInstanceWizardTestProvider
        {
            TestHandler = (_, _) => Task.FromResult(new InstanceConnectionTestResult(false, "身份认证失败", "InvalidAccessKeyId")),
        };
        var configurations = new NewInstanceWizardTestConfigurationService();
        using var model = CreateModel(provider, configurations);
        await model.SubmitCommand.ExecuteAsync();
        MoveToTest(model);
        model.NextCommand.Execute(null);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Equal(3, model.CurrentStep);
        Assert.False(model.HasSuccessfulConnectionTest);
        await model.TestConnectionCommand.ExecuteAsync();
        model.NextCommand.Execute(null);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Equal(3, model.CurrentStep);
        Assert.Equal(0, configurations.CallCount);
    }

    /// <summary>成功测试只针对原配置有效，任何连接信息变更都撤销提交权限。</summary>
    [Theory]
    [InlineData("AccessKeyId")]
    [InlineData("AccessKeySecret")]
    [InlineData("Name")]
    [InlineData("Provider")]
    public async Task ConnectionInformationChange_InvalidatesSuccessfulTest(string field)
    {
        var provider = new NewInstanceWizardTestProvider();
        var alternative = new NewInstanceWizardTestProvider("alternative", "另一服务商");
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(provider, alternative),
            new NewInstanceWizardTestConfigurationService());
        await ReachConfirmationAsync(model);
        Assert.True(model.CanCreate);

        switch (field)
        {
            case "AccessKeyId": GetField(model, "accessKeyId").Value = "changed-access-key"; break;
            case "AccessKeySecret": GetField(model, "accessKeySecret").Value = "changed-secret"; break;
            case "Name": model.Name = "changed-name"; break;
            default: model.SelectedProvider = alternative.Descriptor; break;
        }

        Assert.False(model.CanCreate);
        Assert.False(model.HasSuccessfulConnectionTest);
        Assert.Equal(InstanceConnectionTestState.NotStarted, model.TestState);
        Assert.Empty(model.TestChecks);
    }

    /// <summary>连接器忽略取消令牌时，凭据变化前发出的迟到成功也不能解锁向导。</summary>
    [Fact]
    public async Task InformationChangedDuringTest_IgnoresLateSuccess()
    {
        var completion = NewTestCompletion();
        var provider = new NewInstanceWizardTestProvider { TestHandler = (_, _) => completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken) };
        using var model = CreateModel(provider);
        MoveToTest(model);
        Task running = model.TestConnectionCommand.ExecuteAsync();
        GetField(model, "accessKeySecret").Value = "new-secret";
        Assert.True(provider.LastCancellationToken.IsCancellationRequested);
        completion.SetResult(new InstanceConnectionTestResult(true, "连接成功"));
        await running;
        Assert.False(model.HasSuccessfulConnectionTest);
        Assert.False(model.NextCommand.CanExecute(null));
        Assert.Equal(InstanceConnectionTestState.NotStarted, model.TestState);
    }

    /// <summary>重复测试和步骤切换被禁止，直到当前测试完成。</summary>
    [Fact]
    public async Task TestCommand_PreventsDuplicateRequestsAndStepChanges()
    {
        var completion = NewTestCompletion();
        var provider = new NewInstanceWizardTestProvider { TestHandler = (_, _) => completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken) };
        using var model = CreateModel(provider);
        MoveToTest(model);
        Task running = model.TestConnectionCommand.ExecuteAsync();
        await model.TestConnectionCommand.ExecuteAsync();
        model.PreviousCommand.Execute(null);
        model.NextCommand.Execute(null);
        Assert.True(model.IsTesting);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(3, model.CurrentStep);
        completion.SetResult(new InstanceConnectionTestResult(true, "连接成功"));
        await running;
        Assert.True(model.HasSuccessfulConnectionTest);
    }

    /// <summary>取消测试令牌确实交给连接器，结束后可使用当前填写信息重新测试。</summary>
    [Fact]
    public async Task CancelTest_CancelsProviderRequestAndAllowsRetry()
    {
        var completion = NewTestCompletion();
        var provider = new NewInstanceWizardTestProvider
        {
            TestHandler = (_, token) => completion.Task.WaitAsync(TimeSpan.FromSeconds(5), token),
        };
        using var model = CreateModel(provider);
        MoveToTest(model);
        Task running = model.TestConnectionCommand.ExecuteAsync();
        model.CancelTestCommand.Execute(null);
        Assert.True(provider.LastCancellationToken.IsCancellationRequested);
        await running;
        Assert.False(model.IsTesting);
        Assert.False(model.HasSuccessfulConnectionTest);
        Assert.True(model.TestConnectionCommand.CanExecute(null));
        provider.TestHandler = (_, _) => Task.FromResult(new InstanceConnectionTestResult(true, "连接成功"));
        await model.TestConnectionCommand.ExecuteAsync();
        Assert.Equal(2, provider.CallCount);
        Assert.True(model.HasSuccessfulConnectionTest);
    }

    /// <summary>取消整个向导后，迟到测试不能继续推进或保存。</summary>
    [Fact]
    public async Task CancelWizard_CancelsTestingAndBlocksFutureCreate()
    {
        var completion = NewTestCompletion();
        var provider = new NewInstanceWizardTestProvider { TestHandler = (_, _) => completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken) };
        var configurations = new NewInstanceWizardTestConfigurationService();
        using var model = CreateModel(provider, configurations);
        MoveToTest(model);
        var results = new List<bool>();
        model.CloseRequested += (_, accepted) => results.Add(accepted);
        Task running = model.TestConnectionCommand.ExecuteAsync();
        model.CancelCommand.Execute(null);
        Assert.True(provider.LastCancellationToken.IsCancellationRequested);
        completion.SetResult(new InstanceConnectionTestResult(true, "连接成功"));
        await running;
        model.NextCommand.Execute(null);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Equal(new[] { false }, results);
        Assert.Equal(0, configurations.CallCount);
        Assert.Null(model.CreatedInstance);
        Assert.False(model.TestConnectionCommand.CanExecute(null));
    }

    /// <summary>服务商消息、检查名称和错误码都不能意外回显完整凭据。</summary>
    [Fact]
    public async Task TestResult_RedactsCredentialsAndOffersInformationCorrection()
    {
        var provider = new NewInstanceWizardTestProvider
        {
            TestHandler = (options, _) => Task.FromResult(new InstanceConnectionTestResult(false,
                $"认证失败 {options.Parameters["accessKeyId"]} {options.Parameters["accessKeySecret"]}",
                options.Parameters["accessKeySecret"], [new(options.Parameters["accessKeySecret"], false)])),
        };
        using var model = CreateModel(provider);
        MoveToTest(model);
        await model.TestConnectionCommand.ExecuteAsync();
        Assert.DoesNotContain(GetField(model, "accessKeyId").Value, model.TestMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(GetField(model, "accessKeySecret").Value, model.TestMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(GetField(model, "accessKeySecret").Value, model.TestErrorCode, StringComparison.Ordinal);
        Assert.DoesNotContain(GetField(model, "accessKeySecret").Value, Assert.Single(model.TestChecks).Name, StringComparison.Ordinal);
        Assert.True(model.HasTestFailure);
        model.ModifyInformationCommand.Execute(null);
        Assert.Equal(2, model.CurrentStep);
        Assert.NotEmpty(GetField(model, "accessKeySecret").Value);
    }

    /// <summary>未遵守安全异常契约的连接器也不会将原始异常呈现给用户。</summary>
    [Fact]
    public async Task ProviderException_DisplaysSafeErrorWithoutCommandException()
    {
        var provider = new NewInstanceWizardTestProvider
        {
            TestHandler = (options, _) => Task.FromException<InstanceConnectionTestResult>(
                new InvalidOperationException(options.Parameters["accessKeySecret"])),
        };
        using var model = CreateModel(provider);
        MoveToTest(model);
        await model.TestConnectionCommand.ExecuteAsync();
        Assert.Equal(InstanceConnectionTestState.Failed, model.TestState);
        Assert.DoesNotContain(GetField(model, "accessKeySecret").Value, model.TestMessage, StringComparison.Ordinal);
        Assert.Null(model.TestConnectionCommand.LastException);
    }

    /// <summary>保存失败保持确认步骤和填写内容，重新提交成功后才关闭。</summary>
    [Fact]
    public async Task SaveFailure_PreservesInformationAndAllowsRetry()
    {
        var configurations = new NewInstanceWizardTestConfigurationService
        {
            CreateHandler = (draft, _) => Task.FromException<InstanceSummary>(
                new InvalidOperationException(draft.Connection.Parameters["accessKeySecret"])),
        };
        using var model = CreateModel(configurations: configurations);
        await ReachConfirmationAsync(model);
        string name = model.Name;
        string key = GetField(model, "accessKeyId").Value;
        string secret = GetField(model, "accessKeySecret").Value;
        var results = new List<bool>();
        model.CloseRequested += (_, accepted) => results.Add(accepted);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Equal(5, model.CurrentStep);
        Assert.Equal(name, model.Name);
        Assert.Equal(key, GetField(model, "accessKeyId").Value);
        Assert.Equal(secret, GetField(model, "accessKeySecret").Value);
        Assert.NotEmpty(model.SaveError);
        Assert.DoesNotContain(secret, model.SaveError, StringComparison.Ordinal);
        Assert.Null(model.SubmitCommand.LastException);
        Assert.True(model.CanCreate);
        Assert.Empty(results);

        var saved = new InstanceSummary("retry-saved", "OSS", "OSS", "cloud", []);
        configurations.CreateHandler = (_, _) => Task.FromResult(saved);
        await model.SubmitCommand.ExecuteAsync();
        Assert.Same(saved, model.CreatedInstance);
        Assert.Equal(new[] { true }, results);
        Assert.Equal(2, configurations.CallCount);
        Assert.Empty(model.SaveError);
    }

    /// <summary>创建期间禁止重复提交和取消，成功提交只返回一个安全快照。</summary>
    [Fact]
    public async Task SubmitCommand_PreventsDuplicateSaveAndClosesOnce()
    {
        var completion = new TaskCompletionSource<InstanceSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var configurations = new NewInstanceWizardTestConfigurationService { CreateHandler = (_, _) => completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken) };
        using var model = CreateModel(configurations: configurations);
        await ReachConfirmationAsync(model);
        var results = new List<bool>();
        model.CloseRequested += (_, accepted) => results.Add(accepted);
        Task saving = model.SubmitCommand.ExecuteAsync();
        Assert.True(model.IsSaving);
        Assert.Equal("创建中…", model.ContinueLabel);
        Assert.False(model.CancelCommand.CanExecute(null));
        Assert.False(model.PreviousCommand.CanExecute(null));
        await model.SubmitCommand.ExecuteAsync();
        model.CancelCommand.Execute(null);
        Assert.Equal(1, configurations.CallCount);
        Assert.Empty(results);
        var saved = new InstanceSummary("saved", "OSS", "OSS", "cloud", []);
        completion.SetResult(saved);
        await saving;
        await model.SubmitCommand.ExecuteAsync();
        Assert.Equal(1, configurations.CallCount);
        Assert.Same(saved, model.CreatedInstance);
        Assert.Equal(new[] { true }, results);
        Assert.False(model.IsSaving);
    }

    /// <summary>当前未登录时不能选择账户保存，最终提交明确携带本地范围。</summary>
    [Fact]
    public async Task SaveLocation_RemainsLocalUntilAccountStorageIsAvailable()
    {
        var configurations = new NewInstanceWizardTestConfigurationService();
        using var model = CreateModel(configurations: configurations);
        model.SaveLocation = InstanceSaveLocation.Account;
        Assert.False(model.CanSaveToAccount);
        Assert.Equal(InstanceSaveLocation.Local, model.SaveLocation);
        await ReachConfirmationAsync(model);
        await model.SubmitCommand.ExecuteAsync();
        var draft = Assert.Single(configurations.Drafts);
        Assert.Equal(InstanceSaveLocation.Local, draft.SaveLocation);
        Assert.Equal("OSS regression", draft.Name);
        Assert.Equal("storage", draft.Connection.TypeId);
        Assert.Equal("aliyun-oss", draft.Connection.ProviderId);
        Assert.Equal("LTAIREGRESSION000000", draft.Connection.Parameters["accessKeyId"]);
        Assert.Empty(draft.TagIds);
    }

    /// <summary>短 AK 全部隐藏，长 AK 只展示首尾片段，SK 始终只展示填写状态。</summary>
    [Theory]
    [InlineData("tiny", "••••••••")]
    [InlineData("12345678", "••••••••")]
    [InlineData("LTAIREGRESSION000000", "LTAI••••••••0000")]
    public void Confirmation_MasksCompleteCredentials(string key, string expected)
    {
        using var model = CreateModel();
        GetField(model, "accessKeyId").Value = key;
        GetField(model, "accessKeySecret").Value = "must-never-be-visible";
        Assert.Equal(expected, GetField(model, "accessKeyId").ConfirmationValue);
        Assert.Equal("已填写 · 隐藏", GetField(model, "accessKeySecret").ConfirmationValue);
        Assert.DoesNotContain(key, GetField(model, "accessKeyId").ConfirmationValue, StringComparison.Ordinal);
        Assert.DoesNotContain(GetField(model, "accessKeySecret").Value, GetField(model, "accessKeySecret").ConfirmationValue, StringComparison.Ordinal);
    }

    /// <summary>每个用例建立自己的独立边界替身。</summary>
    private static NewInstanceViewModel CreateModel(NewInstanceWizardTestProvider? provider = null,
        NewInstanceWizardTestConfigurationService? configurations = null)
    {
        var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(provider ?? new()), configurations ?? new());
        model.SelectedType = model.Types.Single(type => type.Id == "storage");
        return model;
    }

    /// <summary>通过声明的稳定标识填写字段，不让测试依赖专属凭据属性。</summary>
    internal static InstanceConfigurationFieldViewModel GetField(NewInstanceViewModel model, string key) =>
        model.Fields.Single(field => field.Key == key);

    /// <summary>按公开命令和字段完成前两步，不修改内部步骤状态。</summary>
    internal static void MoveToTest(NewInstanceViewModel model)
    {
        model.SelectedType ??= model.Types.Single(type => type.Id == "storage");
        model.SelectedProvider ??= model.Providers[0];
        model.NextCommand.Execute(null);
        model.Name = "  OSS regression  ";
        GetField(model, "accessKeyId").Value = "LTAIREGRESSION000000";
        GetField(model, "accessKeySecret").Value = "regression-secret";
        model.NextCommand.Execute(null);
        Assert.Equal(3, model.CurrentStep);
    }

    /// <summary>真实调用连接器替身成功后，通过公开导航到达确认页。</summary>
    internal static async Task ReachConfirmationAsync(NewInstanceViewModel model)
    {
        MoveToTest(model);
        await model.TestConnectionCommand.ExecuteAsync();
        model.NextCommand.Execute(null);
        model.NextCommand.Execute(null);
        Assert.Equal(5, model.CurrentStep);
    }

    /// <summary>显式控制测试完成时机，无需真实时间或网络等待。</summary>
    private static TaskCompletionSource<InstanceConnectionTestResult> NewTestCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
