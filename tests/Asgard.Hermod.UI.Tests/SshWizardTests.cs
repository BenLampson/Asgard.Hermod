using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>SSH 专属正文始终共享类型入口、字段状态、连接测试和保存门禁。</summary>
public sealed class SshWizardTests
{
    /// <summary>三条连接路径各自校验必填字段，成功测试后才能提交。</summary>
    [Theory]
    [InlineData(SshIds.Direct)]
    [InlineData(SshIds.Jump)]
    [InlineData(SshIds.Proxy)]
    [InlineData(SshIds.Bastion)]
    public async Task AllRoutesSelectMethodBeforeConnectionInformation(string route)
    {
        var factory = new SshTestFactory(); var saves = new NewInstanceWizardTestConfigurationService();
        using var model = Create(factory, saves, route); model.NextCommand.Execute(null);
        model.NextCommand.Execute(null); Assert.Equal(2, model.CurrentStep); Assert.False(model.IsInformationComplete);
        Fill(model, route); model.NextCommand.Execute(null); Assert.Equal(3, model.CurrentStep);
        model.NextCommand.Execute(null); Assert.Equal(3, model.CurrentStep);
        await model.TestConnectionCommand.ExecuteAsync(); Assert.True(model.HasSuccessfulConnectionTest);
        Assert.Equal(1, Assert.Single(factory.Connections).DisposeCount);
        model.NextCommand.Execute(null); model.NextCommand.Execute(null); Assert.Equal(5, model.CurrentStep);
        Assert.Equal(Asgard.Hermod.Abstractions.Localization.Text.Get("SshPasswordAuthentication"), Field(model, "auth").ConfirmationValue);
        Assert.DoesNotContain(model.VisibleFields, field => field.Key is "key" or "passphrase");
        await model.SubmitCommand.ExecuteAsync(); var saved = Assert.Single(saves.Drafts);
        Assert.Equal(route, saved.Connection.ProviderId); Assert.Equal("  test-only  ", saved.Connection.Parameters["password"]);
        Assert.Equal(InstanceSaveLocation.Local, saved.SaveLocation);
    }

    /// <summary>认证切换同步隐藏、清除校验并只提交有效认证字段。</summary>
    [Fact]
    public async Task AuthenticationSwitchDoesNotSaveInactiveSecrets()
    {
        var saves = new NewInstanceWizardTestConfigurationService(); using var model = Create(new(), saves);
        model.NextCommand.Execute(null); Fill(model, SshIds.Direct);
        Field(model, "auth").Value = SshIds.Agent; Field(model, "key").Value = "old-key";
        Assert.False(Field(model, "password").IsVisible); Assert.True(model.IsInformationComplete);
        model.NextCommand.Execute(null); await model.TestConnectionCommand.ExecuteAsync();
        model.NextCommand.Execute(null); model.NextCommand.Execute(null); await model.SubmitCommand.ExecuteAsync();
        var parameters = Assert.Single(saves.Drafts).Connection.Parameters;
        Assert.Equal(SshIds.Agent, parameters["auth"]); Assert.False(parameters.ContainsKey("password")); Assert.False(parameters.ContainsKey("key"));
    }

    /// <summary>跳板页切换仅改变视图，目标与跳板认证仍在同一向导状态内。</summary>
    [Fact]
    public void PaneSwitchRetainsIndependentFields()
    {
        using var model = Create(new(), new(), SshIds.Jump); using var editor = new SshConnectionEditorViewModel(model);
        Field(model, "host").Value = "target.example"; editor.IsJumpPane = true; editor.Host.Value = "jump.example";
        editor.Authentication.Value = SshIds.Agent; editor.IsTargetPane = true;
        Assert.Equal("target.example", editor.Host.Value); Assert.Equal(SshIds.Password, editor.Authentication.Value);
        editor.IsJumpPane = true; Assert.Equal("jump.example", editor.Host.Value); Assert.True(editor.UsesAgent);
    }

    /// <summary>修改连接字段撤销此前成功结果，防止未经测试的配置落库。</summary>
    [Fact]
    public async Task ChangedParametersInvalidateVerifiedTest()
    {
        using var model = Create(new(), new()); model.NextCommand.Execute(null); Fill(model, SshIds.Direct);
        model.NextCommand.Execute(null); await model.TestConnectionCommand.ExecuteAsync(); Assert.True(model.HasSuccessfulConnectionTest);
        Field(model, "port").Value = "2222"; Assert.False(model.HasSuccessfulConnectionTest); Assert.False(model.CanCreate);
    }

    internal static NewInstanceViewModel Create(SshTestFactory factory, NewInstanceWizardTestConfigurationService saves, string route = SshIds.Direct)
    {
        var registry = new InstanceProviderRegistry(new[] { SshIds.Direct, SshIds.Jump, SshIds.Bastion, SshIds.Proxy }.Select(id => new SshInstanceProvider(id, factory)));
        var model = new NewInstanceViewModel(registry, saves); model.SelectedType = Assert.Single(model.Types);
        model.SelectedProvider = model.Providers.Single(item => item.Id == route); return model;
    }

    internal static InstanceConfigurationFieldViewModel Field(NewInstanceViewModel model, string key) => model.Fields.Single(field => field.Key == key);
    internal static void Fill(NewInstanceViewModel model, string route)
    {
        model.Name = "Production SSH"; Field(model, "host").Value = "target.example"; Field(model, "user").Value = "tester";
        Field(model, "password").Value = "  test-only  ";
        if (route == SshIds.Jump) { Field(model, "jump.host").Value = "jump.example"; Field(model, "jump.user").Value = "jumper"; Field(model, "jump.auth").Value = SshIds.Agent; }
        if (route == SshIds.Proxy) Field(model, "proxy.host").Value = "proxy.example";
        if (route == SshIds.Bastion) Field(model, "bastion.selection").Value = "2";
    }
}
