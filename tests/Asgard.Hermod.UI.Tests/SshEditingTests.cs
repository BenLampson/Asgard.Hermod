using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>编辑真实配置复用公共保存门禁，连接路径可变而实例身份保持不变。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshEditingTests(WpfTestFixture fixture)
{
    /// <summary>编辑四步流程可切换路径，保留共同字段，修改后必须重新测试才能更新原记录。</summary>
    [Theory]
    [InlineData(SshIds.Jump)]
    [InlineData(SshIds.Proxy)]
    public Task RouteEditRetainsIdentityAndSecretsAndRequiresRetest(string route) => fixture.RunAsync(async () =>
    {
        using var environment = new LocalStorageTestEnvironment(); var factory = new SshTestFactory();
        using var services = new ServiceCollection().AddSingleton<Asgard.Hermod.Ssh.ISshConnectionFactory>(factory)
            .AddHermodCore().AddWindowsSecretProtection().AddSsh().AddLocalStorage(environment.CreateConfiguration()).BuildServiceProvider();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var store = services.GetRequiredService<IInstanceConfigurationService>();
        var parameters = SshProfile.Fields(SshIds.Direct).ToDictionary(field => field.Key, field => field.DefaultValue);
        parameters["host"] = "target.example"; parameters["user"] = "tester"; parameters["password"] = "test-only";
        var saved = await store.CreateAsync(new()
        {
            Name = "Editing SSH",
            Connection = new() { TypeId = SshIds.Type, ProviderId = SshIds.Direct, Parameters = parameters }
        }, TestContext.Current.CancellationToken);
        var original = Assert.Single(services.GetRequiredService<ILocalConfigurationRepository>().GetSnapshot().Instances);
        using var wizard = new NewInstanceViewModel(services.GetRequiredService<IInstanceProviderRegistry>(), store, saved,
            (await store.GetConnectionAsync(saved.Id, TestContext.Current.CancellationToken))!);
        Assert.Equal(4, wizard.Steps.Count); Assert.False(wizard.IsTypeSelectionEditable); Assert.True(wizard.IsProviderSelectionEditable);
        wizard.SelectedProvider = wizard.Providers.Single(item => item.Id == route);
        Assert.Equal("target.example", SshWizardTests.Field(wizard, "host").Value);
        Assert.Equal("test-only", SshWizardTests.Field(wizard, "password").Value);
        if (route == SshIds.Jump)
        {
            SshWizardTests.Field(wizard, "jump.host").Value = "jump.example";
            SshWizardTests.Field(wizard, "jump.user").Value = "jumper";
            SshWizardTests.Field(wizard, "jump.auth").Value = SshIds.Agent;
        }
        else SshWizardTests.Field(wizard, "proxy.host").Value = "proxy.example";
        wizard.NextCommand.Execute(null); wizard.NextCommand.Execute(null);
        Assert.Equal(3, wizard.CurrentStep); Assert.False(wizard.CanCreate);
        await wizard.TestConnectionCommand.ExecuteAsync(); wizard.NextCommand.Execute(null);
        Assert.True(wizard.IsConfirmationStep); Assert.False(wizard.IsSaveLocationStep); Assert.True(wizard.CanCreate);
        Assert.Equal(Text.Get("ConfigurationAppliesToNextConnection"), wizard.ConfirmationHint);
        SshWizardTests.Field(wizard, "port").Value = "2222"; Assert.False(wizard.CanCreate);
        wizard.PreviousCommand.Execute(null); await wizard.TestConnectionCommand.ExecuteAsync(); wizard.NextCommand.Execute(null);
        await wizard.SubmitCommand.ExecuteAsync(); Assert.Equal(saved.Id, wizard.CreatedInstance?.Id);
        var updated = Assert.Single(store.GetInstances()); Assert.Equal(route, updated.ProviderId);
        var restored = (await store.GetConnectionAsync(saved.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal("2222", restored.Parameters["port"]); Assert.Equal("test-only", restored.Parameters["password"]);
        Assert.Equal(original.CreatedAt, Assert.Single(services.GetRequiredService<ILocalConfigurationRepository>().GetSnapshot().Instances).CreatedAt);
    });

    /// <summary>保存反馈不重建现有连接；只有显式重连才读取新端口。</summary>
    [Fact]
    public Task SavedConfigurationAppliesOnlyToNextConnection() => fixture.RunAsync(async () =>
    {
        var store = new SshTestConfigurationService(); var factory = new SshTestFactory(); var dialogs = new SshTestDialogs();
        var summary = new InstanceSummary("ssh-test", "Original", "Original", "terminal", [], TypeId: SshIds.Type, ProviderId: SshIds.Direct);
        var instance = new InstanceItemViewModel(summary);
        await using var model = new SshWorkspaceViewModel(instance, store, factory, dialogs, dialogs);
        await model.InitializeAsync(); var session = model.Session; var original = factory.LastOptions;
        dialogs.Updated = summary with { Name = "Changed", DisplayName = "Changed" };
        store.Connection = new()
        {
            TypeId = SshIds.Type,
            ProviderId = SshIds.Direct,
            Parameters = new Dictionary<string, string>(store.Connection.Parameters) { ["port"] = "2222" }
        };
        await model.EditConnectionCommand.ExecuteAsync();
        Assert.True(model.HasSaveFeedback); Assert.Same(session, model.Session); Assert.Same(original, factory.LastOptions); Assert.Equal(1, factory.Calls);
        await model.DisconnectCommand.ExecuteAsync(); await model.ReconnectCommand.ExecuteAsync();
        Assert.Equal("2222", factory.LastOptions!.Parameters["port"]); Assert.Equal(2, factory.Calls);
        dialogs.Updated = null; await model.EditConnectionCommand.ExecuteAsync(); Assert.False(model.HasSaveFeedback);
    });
}
