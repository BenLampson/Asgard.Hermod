namespace Asgard.Hermod.UI.Tests;

/// <summary>读写验证的部分成功、清理失败及取消结果不能被向导误判为可保存。</summary>
public sealed class NewInstanceReadWriteValidationTests
{
    /// <summary>各服务商说明自己的验证操作，切换类型时不会携带 OSS 的临时对象描述。</summary>
    [Fact]
    public void TestDescription_ChangesWithProviderAndNotifiesBindings()
    {
        var storage = new NewInstanceWizardTestProvider();
        var docker = new NewInstanceWizardTestDockerProvider();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(storage, docker),
            new NewInstanceWizardTestConfigurationService());
        var notifications = new List<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        model.SelectedType = NewInstanceWizardTestProvider.StorageType;
        Assert.Equal(storage.Descriptor.ConnectionTestDescription, model.TestDescription);
        Assert.Contains("写入临时对象", model.TestDescription, StringComparison.Ordinal);

        notifications.Clear();
        model.SelectedType = NewInstanceWizardTestDockerProvider.DockerType;
        Assert.Equal(docker.Descriptor.ConnectionTestDescription, model.TestDescription);
        Assert.DoesNotContain("临时对象", model.TestDescription, StringComparison.Ordinal);
        Assert.Contains(nameof(model.TestDescription), notifications);
    }

    /// <summary>已经读到列表也不能绕过写入和清理失败，失败项目原样展示给用户。</summary>
    [Theory]
    [InlineData(false, true, "oss_probe_write_denied")]
    [InlineData(true, false, "oss_probe_cleanup_failed")]
    public async Task PartialReadWriteValidation_PreventsNextAndCreate(bool writeSucceeded,
        bool cleanupSucceeded, string errorCode)
    {
        var checks = new InstanceConnectionCheck[]
        {
            new("Bucket 列表读取", true), new("临时对象写入", writeSucceeded),
            new("临时对象回读", writeSucceeded), new("临时对象清理", cleanupSucceeded),
        };
        var provider = new NewInstanceWizardTestProvider
        {
            TestHandler = (_, _) => Task.FromResult(new InstanceConnectionTestResult(false,
                "读写验证未完成", errorCode, checks)),
        };
        using var model = CreateModel(provider);
        NewInstanceWizardTests.MoveToTest(model);
        await model.TestConnectionCommand.ExecuteAsync();

        Assert.True(model.HasTestFailure);
        Assert.Equal(checks, model.TestChecks);
        Assert.Equal(errorCode, model.TestErrorCode);
        Assert.False(model.HasSuccessfulConnectionTest);
        Assert.False(model.CanGoNext);
        Assert.False(model.CanCreate);
        model.NextCommand.Execute(null);
        Assert.Equal(3, model.CurrentStep);
    }

    /// <summary>用户取消后仍呈现当前测试的清理失败和脱敏位置，禁止保存或推进。</summary>
    [Fact]
    public async Task CancelledTest_DisplaysCleanupFailureWithoutAllowingNext()
    {
        var completion = NewCompletion();
        var provider = new NewInstanceWizardTestProvider { TestHandler = (_, _) => completion.Task };
        using var model = CreateModel(provider);
        NewInstanceWizardTests.MoveToTest(model);
        Task running = model.TestConnectionCommand.ExecuteAsync();
        model.CancelTestCommand.Execute(null);
        completion.SetResult(new InstanceConnectionTestResult(false,
            "临时对象清理失败：regression-bucket/.hermod-connection-test/probe；regression-secret",
            "oss_probe_cleanup_failed", [new("临时对象清理", false)])
        {
            RequiresAttentionAfterCancellation = true,
        });
        await running;

        Assert.True(model.HasTestFailure);
        Assert.Contains("regression-bucket/.hermod-connection-test/probe", model.TestMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("regression-secret", model.TestMessage, StringComparison.Ordinal);
        Assert.Equal("oss_probe_cleanup_failed", model.TestErrorCode);
        Assert.False(model.IsTesting);
        Assert.False(model.CanGoNext);
        Assert.False(model.CanCreate);
        Assert.True(model.CanTest);
    }

    /// <summary>取消后的成功即使错误地带有关注标记，也不能重新解锁旧测试。</summary>
    [Fact]
    public async Task CancelledTest_IgnoresSuccessfulAttentionResult()
    {
        var completion = NewCompletion();
        var provider = new NewInstanceWizardTestProvider { TestHandler = (_, _) => completion.Task };
        using var model = CreateModel(provider);
        NewInstanceWizardTests.MoveToTest(model);
        Task running = model.TestConnectionCommand.ExecuteAsync();
        model.CancelTestCommand.Execute(null);
        completion.SetResult(new InstanceConnectionTestResult(true, "迟到的成功")
        {
            RequiresAttentionAfterCancellation = true,
        });
        await running;

        Assert.Equal(InstanceConnectionTestState.NotStarted, model.TestState);
        Assert.False(model.HasSuccessfulConnectionTest);
        Assert.False(model.CanGoNext);
        Assert.DoesNotContain("迟到", model.TestMessage, StringComparison.Ordinal);
    }

    /// <summary>旧配置的清理失败不能替换新配置的状态，即使连接器要求取消后提示。</summary>
    [Fact]
    public async Task ChangedConfiguration_IgnoresOldCleanupResult()
    {
        var completion = NewCompletion();
        var provider = new NewInstanceWizardTestProvider { TestHandler = (_, _) => completion.Task };
        using var model = CreateModel(provider);
        NewInstanceWizardTests.MoveToTest(model);
        Task running = model.TestConnectionCommand.ExecuteAsync();
        NewInstanceWizardTests.GetField(model, "accessKeySecret").Value = "new-regression-secret";
        completion.SetResult(new InstanceConnectionTestResult(false, "旧配置的清理失败", "oss_probe_cleanup_failed")
        {
            RequiresAttentionAfterCancellation = true,
        });
        await running;

        Assert.Equal(InstanceConnectionTestState.NotStarted, model.TestState);
        Assert.Empty(model.TestErrorCode);
        Assert.Empty(model.TestChecks);
        Assert.DoesNotContain("旧配置", model.TestMessage, StringComparison.Ordinal);
        Assert.False(model.HasSuccessfulConnectionTest);
    }

    /// <summary>每个用例单独创建连接器与配置边界。</summary>
    private static NewInstanceViewModel CreateModel(NewInstanceWizardTestProvider provider) =>
        new(new NewInstanceWizardTestRegistry(provider), new NewInstanceWizardTestConfigurationService());

    /// <summary>显式控制完成时机，并在命令恢复后验证取消语义。</summary>
    private static TaskCompletionSource<InstanceConnectionTestResult> NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
