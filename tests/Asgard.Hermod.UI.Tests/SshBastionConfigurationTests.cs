using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>菜单配置共享现有保存和编辑路径，并验证两种语言的 WPF 表单。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshBastionConfigurationTests(WpfTestFixture fixture)
{
    /// <summary>只填写一组 SSH 身份和数字，保存后可回读，修改编号撤销连接测试。</summary>
    [Fact]
    public async Task NumberIsValidatedAndSavedWithoutTargetCredentials()
    {
        var saves = new NewInstanceWizardTestConfigurationService();
        using var wizard = SshWizardTests.Create(new(), saves, SshIds.Bastion);
        using var editor = new SshConnectionEditorViewModel(wizard);
        wizard.NextCommand.Execute(null); SshWizardTests.Fill(wizard, SshIds.Bastion);
        Assert.True(editor.IsBastion); Assert.False(editor.HasPanes); Assert.Equal("2", editor.BastionSelection.Value);
        editor.BastionSelection.Value = "1; whoami"; Assert.False(wizard.IsInformationComplete);
        editor.BastionSelection.Value = "2"; Assert.True(wizard.IsInformationComplete);
        wizard.NextCommand.Execute(null); await wizard.TestConnectionCommand.ExecuteAsync(); Assert.True(wizard.HasSuccessfulConnectionTest);
        editor.BastionSelection.Value = "3"; Assert.False(wizard.HasSuccessfulConnectionTest);
        await wizard.TestConnectionCommand.ExecuteAsync(); wizard.NextCommand.Execute(null); wizard.NextCommand.Execute(null);
        await wizard.SubmitCommand.ExecuteAsync();
        var parameters = Assert.Single(saves.Drafts).Connection.Parameters;
        Assert.Equal("3", parameters["bastion.selection"]); Assert.Equal("[vbh]>", parameters["bastion.menuPrompt"]);
        Assert.DoesNotContain(parameters.Keys, key => key.StartsWith("jump.", StringComparison.Ordinal));
    }

    /// <summary>文件和转发入口不可触发网关操作，但终端、监控和 Docker 保持可用。</summary>
    [Fact]
    public Task UnsupportedToolsAreDisabledForBastionOnly() => fixture.RunOffscreenAsync(async () =>
    {
        var configurations = new SshTestConfigurationService
        {
            Connection = new()
            {
                TypeId = SshIds.Type,
                ProviderId = SshIds.Bastion,
                Parameters = new Dictionary<string, string> { ["host"] = "bastion.example", ["user"] = "tester", ["password"] = "test-only", ["bastion.selection"] = "2" },
            },
        };
        var dialogs = new SshTestDialogs();
        await using var model = new SshWorkspaceViewModel(new InstanceItemViewModel(new("bastion-test", "Menu SSH", "Menu SSH", "terminal", [],
            TypeId: SshIds.Type, ProviderId: SshIds.Bastion)), configurations, new SshTestFactory(), dialogs, dialogs);
        await model.InitializeAsync();
        Assert.True(model.IsConnected); Assert.True(model.IsBastion); Assert.False(model.SupportsInstanceTools);
        Assert.False(model.ToggleToolboxCommand.CanExecute("files")); Assert.False(model.ToggleToolboxCommand.CanExecute("transfers"));
        Assert.False(model.AddForwardCommand.CanExecute(null)); Assert.False(model.ShowForwardEditorCommand.CanExecute(null));
        Assert.True(model.NewSessionCommand.CanExecute(null)); Assert.True(model.ToggleMonitorCommand.CanExecute(null));
        Assert.True(model.ToggleDockerCommandsCommand.CanExecute(null));
        Assert.Contains("2", model.RemoteIdentity); Assert.DoesNotContain("tester@", model.RemoteIdentity);
    });

    /// <summary>数字连接方式在中英文下都渲染同一组字段并保留键盘导航。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task EditorRendersBastionNumberInBothLanguages(string language) => fixture.RunOffscreenAsync(() =>
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var wizard = SshWizardTests.Create(new(), new(), SshIds.Bastion);
            SshWizardTests.Fill(wizard, SshIds.Bastion);
            var view = new SshConnectionEditor(wizard);
            using var model = (SshConnectionEditorViewModel)view.DataContext;
            view.Measure(new System.Windows.Size(520, 1200)); view.Arrange(new System.Windows.Rect(0, 0, 520, 1200)); view.UpdateLayout();
            Assert.True(view.DesiredSize.Height > 0); Assert.Equal(Text.Get("SshBastionSelection"), model.BastionSelection.Label);
            Assert.Contains("2", model.RouteSummary); Assert.NotEqual("SshBastionCapabilities", Text.Get("SshBastionCapabilities"));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
        return Task.CompletedTask;
    });
}
