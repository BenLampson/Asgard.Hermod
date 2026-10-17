using Asgard.Hermod.Core;

namespace Asgard.Hermod.RabbitMq.UI.Tests;

[Collection(HeadlessCollection.Name)]
public sealed class WizardTests(HeadlessFixture fixture)
{
    [Fact]
    public Task Categories_AddAProductStep_WithoutChangingPersistedTypeIds() => fixture.RunAsync(() =>
    {
        var registry = new InstanceProviderRegistry([new RabbitMqInstanceProvider(RabbitMqIds.Direct, new FakeRabbitMq()), new RabbitMqInstanceProvider(RabbitMqIds.Ssh, new FakeRabbitMq()), new DatabaseProvider("mysql"), new DatabaseProvider("postgresql"), new DatabaseProvider("redis")]);
        using var wizard = new NewInstanceViewModel(registry, new TestConfigurations());
        Assert.Equal(new[] { "database", "mq" }, wizard.Categories.Select(item => item.Id));
        wizard.SelectedCategory = wizard.Categories.Single(item => item.Id == "mq");
        Assert.True(wizard.IsProductStep); Assert.Equal(RabbitMqIds.Type, wizard.SelectedType!.Id); Assert.Equal(5, wizard.Steps.Count); Assert.Equal(1, wizard.Steps[0].Number);
        wizard.NextCommand.Execute(null); Assert.True(wizard.IsProviderStep); Assert.False(wizard.CanGoNext);
        wizard.SelectedProvider = wizard.Providers.First(); wizard.NextCommand.Execute(null); Assert.True(wizard.IsInformationStep); Assert.True(wizard.HasHostingProviderSelection);
        wizard.PreviousCommand.Execute(null); wizard.PreviousCommand.Execute(null); Assert.True(wizard.IsProductStep);
        wizard.PreviousCommand.Execute(null); Assert.True(wizard.IsChoosingType); Assert.Null(wizard.SelectedType);
        wizard.SelectedCategory = wizard.Categories.Single(item => item.Id == "database"); Assert.Equal(3, wizard.Products.Count);
        wizard.SelectedProduct = wizard.Products.Single(item => item.Id == "redis"); Assert.Equal("redis", wizard.SelectedType!.Id); Assert.True(wizard.IsProductStep);
        return Task.CompletedTask;
    });
    [Fact]
    public Task SchemeDefaults_ChangeOnlyUntouchedPorts_AndRetainSshFields() => fixture.RunAsync(() =>
    {
        var registry = new InstanceProviderRegistry([new RabbitMqInstanceProvider(RabbitMqIds.Direct, new FakeRabbitMq()), new RabbitMqInstanceProvider(RabbitMqIds.Ssh, new FakeRabbitMq())]);
        using var wizard = new NewInstanceViewModel(registry, new TestConfigurations()); wizard.SelectedType = wizard.Types.Single(); wizard.SelectedProvider = wizard.Providers.Single(item => item.Id == RabbitMqIds.Ssh);
        using var editor = new RabbitMqConnectionEditorViewModel(wizard);
        var scheme = wizard.Fields.Single(item => item.Key == "scheme"); var port = wizard.Fields.Single(item => item.Key == "port");
        Assert.Equal("15671", port.Value); scheme.Value = "http"; Assert.Equal("15672", port.Value); Assert.True(editor.IsPlaintext);
        port.Value = "18080"; scheme.Value = "https"; Assert.Equal("18080", port.Value);
        editor.Pane = "ssh"; Assert.Contains(editor.Fields, field => field.Key == "ssh.host");
        return Task.CompletedTask;
    });
    private sealed class DatabaseProvider(string type) : IInstanceProvider
    {
        public InstanceProviderDescriptor Descriptor => new(type + ".direct", type, type, "database", new(type, type, "", "database"), []);
        public Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default) => Task.FromResult(new InstanceConnectionTestResult(true, "ok"));
    }
}
