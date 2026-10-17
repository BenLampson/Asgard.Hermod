namespace Asgard.Hermod.RabbitMq.UI.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessFixture>
{
    public const string Name = "RabbitMQ headless WPF";
}
