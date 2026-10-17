using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Core;
using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.PostgreSql.Tests;

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
            var registry = new InstanceProviderRegistry([new PostgreSqlInstanceProvider(PostgreSqlIds.Direct, new NoConnections()), new PostgreSqlInstanceProvider(PostgreSqlIds.Ssh, new NoConnections())]);
            using var wizard = new NewInstanceViewModel(registry, new Configurations());
            wizard.SelectedType = registry.Types.Single(); wizard.SelectedProvider = wizard.Providers.Single(item => item.Id == PostgreSqlIds.Ssh);
            var view = new PostgreSqlConnectionEditor(wizard); var model = Assert.IsType<PostgreSqlConnectionEditorViewModel>(view.DataContext);
            Assert.Contains(model.Fields, item => item.Key == "password"); Assert.DoesNotContain(model.Fields, item => item.Key == "ssh.password");
            model.Pane = "ssh"; Assert.Contains(model.Fields, item => item.Key == "ssh.password");
            model.Pane = "security"; Assert.Equal("verify-full", wizard.Fields.Single(item => item.Key == "tls").Value);
            wizard.Fields.Single(item => item.Key == "tls").Value = "required"; Assert.True(model.HasTlsWarning);
            wizard.Name = "PostgreSQL test"; model.Pane = "connection"; Assert.Equal("PostgreSQL test", wizard.Name);
            Assert.Equal(5, wizard.Steps.Count); model.Dispose();
            Assert.NotEqual("PostgreSqlTlsWarning", Text.Get("PostgreSqlTlsWarning"));
        }
        finally { CultureInfo.CurrentUICulture = old; }
        return Task.CompletedTask;
    });

    private sealed class NoConnections : IPostgreSqlSessionFactory
    { public Task<IPostgreSqlSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
    private sealed class Configurations : IInstanceConfigurationService
    {
        public IReadOnlyList<InstanceSummary> GetInstances() => [];
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(null);
    }
}
