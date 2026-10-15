using System.Text.Json;
using Asgard.Hermod.Agent;
using Xunit;

namespace Asgard.Hermod.Agent.Tests;

public sealed class AgentConfigurationTests
{
    [Fact]
    public void Configuration_ReplacesAtomicSnapshotAndDoesNotSerializeKey()
    {
        var configuration = new AgentConfiguration();
        Assert.False(configuration.Current.IsConfigured);
        configuration.Update("https://models.example/v1", "model-one", "private-key");
        var snapshot = configuration.Current;
        configuration.Update("http://127.0.0.1:11434/v1", "local-model", "");
        Assert.True(configuration.Current.IsConfigured);
        Assert.Equal("model-one", snapshot.Model); Assert.Equal("private-key", snapshot.ApiKey);
        Assert.DoesNotContain("private-key", snapshot.ToString());
        Assert.DoesNotContain("private-key", JsonSerializer.Serialize(snapshot));
        Assert.DoesNotContain("ApiKey", JsonSerializer.Serialize(snapshot));
        configuration.Update("", "", ""); Assert.False(configuration.Current.IsConfigured);
    }

    [Theory]
    [InlineData("https://model.example/v1")]
    [InlineData("https://model.example/chat/completions")]
    [InlineData("http://localhost:1234/v1")]
    [InlineData("http://[::1]:1234/v1")]
    public void Endpoint_AllowsTlsAndLocalModels(string endpoint) => Assert.True(new AgentModelOptions(endpoint, "model").IsConfigured);

    [Theory]
    [InlineData("http://remote.example/v1")]
    [InlineData("https://user:secret@models.example/v1")]
    [InlineData("https://models.example/v1?api_key=secret")]
    [InlineData("https://models.example/v1#fragment")]
    [InlineData("file:///tmp/model")]
    public void Endpoint_RejectsUnsafeAddresses(string endpoint) => Assert.False(new AgentModelOptions(endpoint, "model").IsConfigured);

    [Fact]
    public void InvalidUpdate_RetainsLastValidConfiguration()
    {
        var configuration = new AgentConfiguration(); configuration.Update("https://model.example/v1", "model", "secret");
        var original = configuration.Current;
        var error = Assert.Throws<InvalidOperationException>(() => configuration.Update("http://remote.example/v1", "model", "secret"));
        Assert.Equal("AgentInvalidEndpoint", error.Data["Hermod.ResourceKey"]); Assert.Same(original, configuration.Current);
    }
}
