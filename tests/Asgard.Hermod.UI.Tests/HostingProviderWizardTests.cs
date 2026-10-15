namespace Asgard.Hermod.UI.Tests;

/// <summary>连接向导的品牌是独立元数据，三类实例在新建和编辑时使用相同约定。</summary>
public sealed class HostingProviderWizardTests
{
    /// <summary>已验证连接修改品牌仍可保存，品牌不会混入协议参数。</summary>
    [Theory]
    [InlineData("ssh")]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    [InlineData("redis")]
    public async Task ConfirmedConnection_ChangesBrandWithoutInvalidatingTest(string type)
    {
        var provider = new NewInstanceWizardTestProvider("transport", "Test route", new(type, type, "", "terminal"));
        var configurations = new NewInstanceWizardTestConfigurationService();
        var original = new InstanceSummary("brand-edit", "Test instance", "Test instance", "terminal", [],
            TypeId: type, ProviderId: "transport")
        { HostingProviderId = "aliyun" };
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(provider), configurations, original, new()
        {
            TypeId = type,
            ProviderId = "transport",
            Parameters = new Dictionary<string, string> { ["accessKeyId"] = "TestKey", ["accessKeySecret"] = "test-secret" }
        });
        Assert.True(model.HasHostingProviderSelection);
        Assert.Equal("aliyun", model.HostingProviderId);
        model.NextCommand.Execute(null); model.NextCommand.Execute(null);
        await model.TestConnectionCommand.ExecuteAsync();
        model.NextCommand.Execute(null); model.NextCommand.Execute(null);
        Assert.True(model.CanCreate);

        model.HostingProviderId = "volcengine";

        Assert.True(model.HasSuccessfulConnectionTest);
        Assert.True(model.CanCreate);
        await model.SubmitCommand.ExecuteAsync();
        var update = Assert.Single(configurations.Updates);
        Assert.Equal(original.Id, update.InstanceId);
        Assert.Equal("volcengine", update.Draft.HostingProviderId);
        Assert.Equal(type, update.Draft.Connection.TypeId);
        Assert.Equal("transport", update.Draft.Connection.ProviderId);
        Assert.Equal("test-secret", update.Draft.Connection.Parameters["accessKeySecret"]);
        Assert.False(update.Draft.Connection.Parameters.ContainsKey(InstanceHostingProviders.MetadataKey));
        Assert.Equal(1, provider.CallCount);
    }
}
