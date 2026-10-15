using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.UI.Tests;

/// <summary>连接方式与表单始终为独立页面，保持测试门禁、回退和独立草稿。</summary>
public sealed class ConnectionMethodWorkflowTests
{
    /// <summary>首步仅选连接方式，返回首步保留表单，再返回类型入口才清空草稿。</summary>
    [Fact]
    public void NewSsh_SelectsMethodBeforeInformationAndClearsOnReturnToTypePicker()
    {
        using var wizard = SshWizardTests.Create(new(), new());
        Assert.Equal(SshIds.Direct, wizard.SelectedProvider!.Id);
        Assert.True(wizard.IsProviderStep);
        Assert.False(wizard.IsInformationStep);
        Assert.Equal(new[] { 1, 2, 3, 4 }, wizard.Steps.Select(step => step.Number));
        Assert.Equal(Text.Format("WizardStepOfTotal", 1, 4), wizard.StepCountText);
        wizard.NextCommand.Execute(null);
        Assert.True(wizard.IsInformationStep);
        Assert.Equal(Text.Format("WizardStepOfTotal", 2, 4), wizard.StepCountText);
        SshWizardTests.Fill(wizard, SshIds.Direct);
        var password = SshWizardTests.Field(wizard, "password");
        wizard.PreviousCommand.Execute(null);
        Assert.True(wizard.IsProviderStep);
        Assert.Equal("  test-only  ", password.Value);
        wizard.PreviousCommand.Execute(null);
        Assert.True(wizard.IsChoosingType);
        Assert.Empty(wizard.Fields);
        Assert.Empty(password.Value);
        Assert.Empty(wizard.Name);
        Assert.False(wizard.HasSuccessfulConnectionTest);
    }

    /// <summary>返回方式页改变路径后保留共同目标参数，但必须再次测试。</summary>
    [Fact]
    public async Task RouteChange_RetainsCommonInputsAndInvalidatesSuccessfulTest()
    {
        using var wizard = SshWizardTests.Create(new(), new());
        wizard.NextCommand.Execute(null);
        SshWizardTests.Fill(wizard, SshIds.Direct);
        wizard.NextCommand.Execute(null);
        await wizard.TestConnectionCommand.ExecuteAsync();
        wizard.NextCommand.Execute(null);
        Assert.True(wizard.IsConfirmationStep);
        Assert.False(wizard.IsSaveLocationStep);
        Assert.Equal(Text.Format("WizardStepOfTotal", 4, 4), wizard.StepCountText);
        wizard.PreviousCommand.Execute(null);
        Assert.True(wizard.IsTestStep);
        wizard.PreviousCommand.Execute(null);
        Assert.True(wizard.IsInformationStep);
        wizard.PreviousCommand.Execute(null);
        Assert.True(wizard.IsProviderStep);
        wizard.SelectedProvider = wizard.Providers.Single(provider => provider.Id == SshIds.Jump);
        Assert.Equal("  test-only  ", SshWizardTests.Field(wizard, "password").Value);
        Assert.False(wizard.HasSuccessfulConnectionTest);
        Assert.False(wizard.CanCreate);
        Assert.True(wizard.IsProviderStep);
        wizard.NextCommand.Execute(null);
        Assert.True(wizard.IsInformationStep);
    }

    /// <summary>所有已注册类型都在独立首步明确选项，选择卡片不会自动跳到表单。</summary>
    [Theory]
    [InlineData("ssh")]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    [InlineData("redis")]
    [InlineData("storage")]
    public void EveryType_SeparatesProviderChoiceAndInformation(string type)
    {
        var descriptor = type switch
        {
            "ssh" => SshProfile.Descriptor(SshIds.Direct).Type,
            "mysql" => Asgard.Hermod.MySql.MySqlProfile.Descriptor(Asgard.Hermod.MySql.MySqlIds.Direct).Type,
            "postgresql" => Asgard.Hermod.PostgreSql.PostgreSqlProfile.Descriptor(Asgard.Hermod.PostgreSql.PostgreSqlIds.Direct).Type,
            "redis" => Asgard.Hermod.Redis.RedisProfile.Descriptor(Asgard.Hermod.Redis.RedisIds.Standalone).Type,
            _ => new NewInstanceWizardTestProvider().Descriptor.Type,
        };
        var first = new NewInstanceWizardTestProvider("first", "First", descriptor);
        var second = new NewInstanceWizardTestProvider("second", "Second", descriptor);
        using var wizard = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(first, second), new NewInstanceWizardTestConfigurationService());
        wizard.SelectedType = Assert.Single(wizard.Types);
        Assert.True(wizard.IsProviderStep);
        Assert.False(wizard.IsInformationStep);
        Assert.False(wizard.CanGoNext);
        wizard.SelectedProvider = second.Descriptor;
        Assert.True(wizard.IsProviderStep);
        Assert.True(wizard.CanGoNext);
        wizard.NextCommand.Execute(null);
        Assert.True(wizard.IsInformationStep);
        Assert.False(wizard.IsProviderStep);
        wizard.PreviousCommand.Execute(null);
        Assert.True(wizard.IsProviderStep);
        Assert.Same(second.Descriptor, wizard.SelectedProvider);
    }
}
