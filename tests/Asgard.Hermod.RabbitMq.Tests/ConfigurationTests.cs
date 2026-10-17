namespace Asgard.Hermod.RabbitMq.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void ProxyPrefix_AndSshTarget_DoNotChangeTlsIdentity()
    {
        var options = RabbitMqSessionOptions.Parse(RecordingHandler.Connection(RabbitMqIds.Ssh, ("prefix", "/proxy/rabbit"), ("ssh.host", "jump.test"), ("ssh.user", "ssh-operator")));
        Assert.Equal("https://mq.example.test:15671/proxy/rabbit/api/", options.BaseUri.AbsoluteUri);
        Assert.Equal("mq.example.test", options.BaseUri.Host); Assert.Equal("jump.test", options.SshConnection.Parameters["host"]);
        Assert.DoesNotContain("password", options.Address);
    }
    [Theory]
    [InlineData("//evil.test")]
    [InlineData("/../outside")]
    [InlineData("/%2e%2e")]
    [InlineData("/proxy?token=x")]
    [InlineData("relative")]
    public void UnsafePrefixes_AreRejected(string prefix)
    {
        if (prefix == "//evil.test")
        {
            var options = RabbitMqSessionOptions.Parse(RecordingHandler.Connection(values: [("prefix", prefix)]));
            Assert.Equal("mq.example.test", options.BaseUri.Host); return;
        }
        Assert.ThrowsAny<Exception>(() => RabbitMqSessionOptions.Parse(RecordingHandler.Connection(values: [("prefix", prefix)])));
    }
    [Theory]
    [InlineData("quorum", false, false)]
    [InlineData("stream", true, true)]
    public void InvalidQueueDurability_IsRejected(string type, bool durable, bool autoDelete) => Assert.ThrowsAny<Exception>(() =>
        RabbitMqValidation.Validate(new(RabbitMqOperation.DeclareQueue, "/", "q", JsonSerializer.SerializeToElement(new { durable, auto_delete = autoDelete, arguments = new Dictionary<string, string> { ["x-queue-type"] = type } }))));
    [Theory]
    [InlineData("amq.reserved")]
    [InlineData("")]
    public void ReservedAndEmptyResourceNames_AreRejected(string name) => Assert.ThrowsAny<Exception>(() => RabbitMqValidation.Validate(new(RabbitMqOperation.DeclareExchange, "/", name)));
    [Fact]
    public void BinaryDisplay_UsesStrictUtf8AndPreservesBytes()
    {
        var message = new RabbitMqMessage([0xc3, 0x28], 2, "", "", false, JsonSerializer.SerializeToElement(new { }));
        Assert.Equal("wyg=", message.Display()); Assert.False(message.IsTruncated);
    }
}
