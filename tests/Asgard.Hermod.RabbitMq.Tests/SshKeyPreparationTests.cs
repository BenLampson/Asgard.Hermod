using System.Security.Cryptography;
using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.RabbitMq.Tests;

/// <summary>隧道私钥必须脱离原设备路径，且不得改变 MQ 身份或其他认证方式。</summary>
public sealed class SshKeyPreparationTests
{
    /// <summary>测试后删除源文件，重复保存仍使用同一份私钥及口令，MQ 参数保持原样。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportedKey_SurvivesSourceDeletionAndPreservesMqSettings(bool encrypted)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pem");
        const string passphrase = "  test-key-passphrase  ";
        using var key = RSA.Create(2048);
        string pem = encrypted ? key.ExportEncryptedPkcs8PrivateKeyPem(passphrase,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000)) : key.ExportPkcs8PrivateKeyPem();
        await File.WriteAllTextAsync(path, pem, TestContext.Current.CancellationToken);
        try
        {
            var original = Connection(SshIds.PrivateKey, path, ("ssh.passphrase", passphrase),
                ("prefix", "/rabbit"), ("timeout", "30"));
            var provider = new RabbitMqInstanceProvider(RabbitMqIds.Ssh, new NeverConnect());
            var prepared = await provider.PrepareConnectionAsync(original, TestContext.Current.CancellationToken);
            File.Delete(path);
            Assert.Equal(path, original.Parameters["ssh.key"]);
            Assert.True(InstanceFileContent.IsImported(prepared.Parameters["ssh.key"]));
            char[] decoded = InstanceFileContent.Decode(prepared.Parameters["ssh.key"]);
            try { Assert.Equal(pem, new string(decoded)); } finally { Array.Clear(decoded); }
            foreach (var pair in original.Parameters.Where(pair => pair.Key != "ssh.key"))
                Assert.Equal(pair.Value, prepared.Parameters[pair.Key]);
            var repeated = await provider.PrepareConnectionAsync(prepared, TestContext.Current.CancellationToken);
            Assert.Equal(prepared.Parameters["ssh.key"], repeated.Parameters["ssh.key"]);
            Assert.Equal(RabbitMqIds.Type, repeated.TypeId);
            Assert.Equal(RabbitMqIds.Ssh, repeated.ProviderId);
            var session = RabbitMqSessionOptions.Parse(repeated);
            Assert.Equal("https://mq.example.test:15671/rabbit/api/", session.BaseUri.AbsoluteUri);
            var target = SshConnectionOptions.Parse(session.SshConnection).Target;
            Assert.Equal(prepared.Parameters["ssh.key"], target.KeyPath);
            Assert.Equal(passphrase, target.Passphrase);
            Assert.DoesNotContain(pem, target.ToString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>密码与 Agent 认证不读取残留私钥路径，也不改写密码空白。</summary>
    [Theory]
    [InlineData(SshIds.Password)]
    [InlineData(SshIds.Agent)]
    public async Task OtherAuthentication_DoesNotReadInactiveKey(string authentication)
    {
        var provider = new RabbitMqInstanceProvider(RabbitMqIds.Ssh, new NeverConnect());
        var prepared = await provider.PrepareConnectionAsync(Connection(authentication, "missing-unused-key",
            ("ssh.password", "  ssh-password  ")), TestContext.Current.CancellationToken);
        var target = SshConnectionOptions.Parse(RabbitMqSessionOptions.Parse(prepared).SshConnection).Target;
        Assert.Equal(authentication, target.Authentication);
        Assert.Empty(target.KeyPath);
        Assert.Equal("  ssh-password  ", prepared.Parameters["ssh.password"]);
    }

    /// <summary>直连不要求 SSH 主机或设备文件可用。</summary>
    [Fact]
    public async Task DirectConnection_DoesNotPrepareSsh()
    {
        var provider = new RabbitMqInstanceProvider(RabbitMqIds.Direct, new NeverConnect());
        var original = RecordingHandler.Connection(values: [("ssh.auth", SshIds.PrivateKey), ("ssh.key", "missing-unused-key")]);
        var prepared = await provider.PrepareConnectionAsync(original, TestContext.Current.CancellationToken);
        Assert.Equal(original.Parameters, prepared.Parameters);
    }

    /// <summary>私钥缺失时阻止保存，安全错误不会泄漏路径。</summary>
    [Fact]
    public async Task MissingKey_FailsWithSafeResourceError()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pem");
        var provider = new RabbitMqInstanceProvider(RabbitMqIds.Ssh, new NeverConnect());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.PrepareConnectionAsync(Connection(SshIds.PrivateKey, path), TestContext.Current.CancellationToken));
        Assert.Equal("SshKeyImportFailed", error.Data["Hermod.ResourceKey"]);
        Assert.DoesNotContain(path, error.Message);
    }

    /// <summary>已取消的测试不能继续读取设备文件。</summary>
    [Fact]
    public async Task Cancellation_StopsPreparationBeforeImport()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new RabbitMqInstanceProvider(RabbitMqIds.Ssh, new NeverConnect());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.PrepareConnectionAsync(Connection(SshIds.PrivateKey, "missing-unused-key"), cancellation.Token));
    }

    /// <summary>MQ 与 SSH 身份独立，使用虚构的离线测试端点。</summary>
    private static InstanceConnectionOptions Connection(string authentication, string key, params (string Key, string Value)[] values) =>
        RecordingHandler.Connection(RabbitMqIds.Ssh, [("ssh.host", "jump.example.test"), ("ssh.user", "ssh-operator"),
            ("ssh.auth", authentication), ("ssh.key", key), .. values]);

    /// <summary>预处理不得访问远程 MQ 或 SSH 服务。</summary>
    private sealed class NeverConnect : IRabbitMqSessionFactory
    {
        public Task<IRabbitMqSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
