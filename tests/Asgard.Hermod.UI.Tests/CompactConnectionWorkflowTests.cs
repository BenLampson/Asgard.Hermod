using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.UI.Tests;

/// <summary>合并连接流程仍保持测试门禁、回退和独立草稿。</summary>
public sealed class CompactConnectionWorkflowTests
{
    [Fact]
    public void NewSsh_DefaultsToDirectInformationAndClearsOnReturnToTypePicker()
    {
        using var wizard = SshWizardTests.Create(new(), new());
        Assert.Equal(SshIds.Direct, wizard.SelectedProvider!.Id);
        Assert.True(wizard.IsInformationStep);
        Assert.False(wizard.IsRouteSelectionExpanded);
        Assert.Equal(new[] { 1, 2, 3 }, wizard.Steps.Select(step => step.Number));
        Assert.Equal(Text.Format("WizardStepOfTotal", 1, 3), wizard.StepCountText);
        SshWizardTests.Fill(wizard, SshIds.Direct);
        var password = SshWizardTests.Field(wizard, "password");
        wizard.PreviousCommand.Execute(null);
        Assert.True(wizard.IsChoosingType);
        Assert.Empty(wizard.Fields);
        Assert.Empty(password.Value);
        Assert.Empty(wizard.Name);
        Assert.False(wizard.HasSuccessfulConnectionTest);
    }

    [Fact]
    public async Task RouteChange_RetainsCommonInputsAndInvalidatesSuccessfulTest()
    {
        using var wizard = SshWizardTests.Create(new(), new());
        SshWizardTests.Fill(wizard, SshIds.Direct);
        wizard.NextCommand.Execute(null);
        await wizard.TestConnectionCommand.ExecuteAsync();
        wizard.NextCommand.Execute(null);
        Assert.True(wizard.IsConfirmationStep);
        Assert.False(wizard.IsSaveLocationStep);
        Assert.Equal(Text.Format("WizardStepOfTotal", 3, 3), wizard.StepCountText);
        wizard.PreviousCommand.Execute(null);
        Assert.True(wizard.IsTestStep);
        wizard.PreviousCommand.Execute(null);
        Assert.True(wizard.IsInformationStep);
        wizard.SelectedProvider = wizard.Providers.Single(provider => provider.Id == SshIds.Jump);
        Assert.Equal("  test-only  ", SshWizardTests.Field(wizard, "password").Value);
        Assert.False(wizard.HasSuccessfulConnectionTest);
        Assert.False(wizard.CanCreate);
        Assert.True(wizard.IsInformationStep);
    }
}
