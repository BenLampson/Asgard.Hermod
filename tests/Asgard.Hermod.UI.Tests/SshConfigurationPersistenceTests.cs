using System.Text;
using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.UI.Tests;

/// <summary>共享配置持久化同样适用于 SSH，不复制存储和凭据保护实现。</summary>
public sealed class SshConfigurationPersistenceTests
{
    /// <summary>密码原始空格、目标与跳板分别加密保存；未使用的私钥字段被删除。</summary>
    [Theory]
    [InlineData(SshIds.Jump)]
    [InlineData(SshIds.Bastion)]
    public async Task SshCredentialsUseExistingEncryptedStoreAndNavigationIsSafe(string route)
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = new ServiceCollection().AddSingleton<ISshConnectionFactory>(new SshTestFactory())
            .AddSingleton<ISshHostKeyPrompt>(new Asgard.Hermod.Ssh.UI.Services.WindowsSshHostKeyPrompt(() => null))
            .AddHermodCore().AddWindowsSecretProtection().AddSsh().AddLocalStorage(environment.CreateConfiguration())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var registry = services.GetRequiredService<IInstanceProviderRegistry>(); Assert.Equal(4, registry.GetProviders(SshIds.Type).Count);
        var parameters = SshProfile.Fields(route).ToDictionary(field => field.Key, field => field.DefaultValue);
        parameters["host"] = "target.example"; parameters["user"] = "tester"; parameters["password"] = "  secret-to-encrypt  ";
        parameters["key"] = "old-unused-key";
        if (route == SshIds.Jump) { parameters["jump.host"] = "jump.example"; parameters["jump.user"] = "jumper"; parameters["jump.password"] = " independent-secret "; }
        if (route == SshIds.Bastion) parameters["bastion.selection"] = "2";
        var service = services.GetRequiredService<IInstanceConfigurationService>();
        var saved = await service.CreateAsync(new()
        {
            Name = "SSH encryption test",
            Connection = new() { TypeId = SshIds.Type, ProviderId = route, Parameters = parameters },
        }, TestContext.Current.CancellationToken);
        var connection = Assert.IsType<InstanceConnectionOptions>(await service.GetConnectionAsync(saved.Id, TestContext.Current.CancellationToken));
        Assert.Equal(route, connection.ProviderId); Assert.Equal("  secret-to-encrypt  ", connection.Parameters["password"]);
        if (route == SshIds.Jump) Assert.Equal(" independent-secret ", connection.Parameters["jump.password"]);
        else
        {
            Assert.Equal("2", connection.Parameters["bastion.selection"]);
            Assert.Equal("[vbh]>", connection.Parameters["bastion.menuPrompt"]);
            Assert.DoesNotContain(connection.Parameters.Keys, key => key.StartsWith("jump.", StringComparison.Ordinal));
        }
        Assert.False(connection.Parameters.ContainsKey("key")); Assert.False(connection.Parameters.ContainsKey("jump.key"));
        var stored = Assert.Single(services.GetRequiredService<ILocalConfigurationRepository>().GetSnapshot(TestContext.Current.CancellationToken).Instances);
        Assert.DoesNotContain("secret-to-encrypt", Encoding.UTF8.GetString(stored.ProtectedParameters));
        Assert.DoesNotContain("secret", saved.ToString());
    }
}
