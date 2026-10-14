using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Core;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class ConnectionTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task ConnectionForm_UsesSharedFieldsWithSeparateSshCredentials(string culture) => fixture.RunAsync(() =>
    {
        var old = CultureInfo.CurrentUICulture; CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var registry = new InstanceProviderRegistry([new MySqlInstanceProvider(MySqlIds.Direct, new NoConnections()), new MySqlInstanceProvider(MySqlIds.Ssh, new NoConnections())]);
            using var wizard = new NewInstanceViewModel(registry, new Configurations());
            wizard.SelectedType = registry.Types.Single(); wizard.SelectedProvider = wizard.Providers.Single(item => item.Id == MySqlIds.Ssh);
            var view = new MySqlConnectionEditor(wizard); var model = Assert.IsType<MySqlConnectionEditorViewModel>(view.DataContext);
            Assert.Contains(model.Fields, item => item.Key == "password"); Assert.DoesNotContain(model.Fields, item => item.Key == "ssh.password");
            model.Pane = "ssh"; Assert.Contains(model.Fields, item => item.Key == "ssh.password");
            model.Pane = "security"; Assert.Equal("verify-full", wizard.Fields.Single(item => item.Key == "tls").Value);
            wizard.Fields.Single(item => item.Key == "tls").Value = "required"; Assert.True(model.HasTlsWarning);
            wizard.Name = "MySQL test"; model.Pane = "connection"; Assert.Equal("MySQL test", wizard.Name);
            Assert.Equal(5, wizard.Steps.Count); model.Dispose();
            Assert.NotEqual("MySqlTlsWarning", Text.Get("MySqlTlsWarning"));
        }
        finally { CultureInfo.CurrentUICulture = old; }
        return Task.CompletedTask;
    });

    private sealed class NoConnections : IMySqlSessionFactory
    { public Task<IMySqlSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
    private sealed class Configurations : IInstanceConfigurationService
    {
        public IReadOnlyList<InstanceSummary> GetInstances() => [];
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(null);
    }
}
