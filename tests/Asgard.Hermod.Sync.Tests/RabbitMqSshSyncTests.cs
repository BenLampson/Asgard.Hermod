using System.Security.Cryptography;
using Asgard.Hermod.Core;
using Asgard.Hermod.RabbitMq;
using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.Sync.Tests;

/// <summary>经过实际同步 API 与 SQLite 存储验证 MQ 隧道凭据的跨设备可移植性。</summary>
public sealed class RabbitMqSshSyncTests
{
    /// <summary>另一设备只下载密文配置，源文件删除后仍可恢复并解锁相同私钥。</summary>
    [Fact]
    public async Task ImportedKey_RoundTripsThroughCloudApiWithoutSourceFileOrPlaintextSecrets()
    {
        const string syncPassword = "mq-sync-test-password";
        const string passphrase = "  mq-key-passphrase  ";
        const string mqPassword = "mq-account-password";
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pem");
        using var key = RSA.Create(2048);
        string pem = key.ExportEncryptedPkcs8PrivateKeyPem(passphrase,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000));
        await File.WriteAllTextAsync(path, pem, TestContext.Current.CancellationToken);
        try
        {
            var provider = new RabbitMqInstanceProvider(RabbitMqIds.Ssh, new NeverConnect());
            var registry = new InstanceProviderRegistry([provider]);
            var prepared = await provider.PrepareConnectionAsync(new()
            {
                TypeId = RabbitMqIds.Type,
                ProviderId = RabbitMqIds.Ssh,
                Parameters = new Dictionary<string, string>
                {
                    ["host"] = "mq.example.test",
                    ["user"] = "mq-user",
                    ["password"] = mqPassword,
                    ["ssh.host"] = "jump.example.test",
                    ["ssh.user"] = "ssh-user",
                    ["ssh.auth"] = SshIds.PrivateKey,
                    ["ssh.key"] = path,
                    ["ssh.passphrase"] = passphrase
                }
            }, TestContext.Current.CancellationToken);
            File.Delete(path);
            var snapshot = new WorkspaceSnapshot(1, [new("mq-one", "MQ", prepared.TypeId, prepared.ProviderId,
                prepared.Parameters, DateTime.UnixEpoch, [])], []);
            var encoded = new ConfigurationCodec(registry).Encode(snapshot, new(1, [], []),
                new("0", null, [], [], null), syncPassword, "owner");
            Assert.Equal("mq.example.test", encoded.Instances[0].Parameters["host"]);
            Assert.Equal("jump.example.test", encoded.Instances[0].Parameters["ssh.host"]);
            Assert.DoesNotContain("ssh.key", encoded.Instances[0].Parameters.Keys);
            Assert.DoesNotContain("ssh.passphrase", encoded.Instances[0].Parameters.Keys);
            Assert.DoesNotContain("password", encoded.Instances[0].Parameters.Keys);

            await using var host = new ApiTestHost();
            await host.InitializeAsync();
            var transport = new ConfigurationTransport(new SyncTestClientFactory(host));
            string token = host.Token();
            await transport.WriteAsync(token, new("0", encoded), TestContext.Current.CancellationToken);
            var downloaded = await transport.ReadAsync(token, TestContext.Current.CancellationToken);
            string wire = JsonSerializer.Serialize(downloaded);
            Assert.DoesNotContain(prepared.Parameters["ssh.key"], wire);
            Assert.DoesNotContain(passphrase, wire);
            Assert.DoesNotContain(mqPassword, wire);
            Assert.DoesNotContain("BEGIN ENCRYPTED PRIVATE KEY", wire);

            var otherProvider = new RabbitMqInstanceProvider(RabbitMqIds.Ssh, new NeverConnect());
            var restored = new ConfigurationCodec(new InstanceProviderRegistry([otherProvider]))
                .Decode(downloaded, syncPassword, "owner");
            var instance = Assert.Single(restored.Instances);
            var otherConnection = await otherProvider.PrepareConnectionAsync(new()
            {
                TypeId = instance.TypeId,
                ProviderId = instance.ProviderId,
                Parameters = instance.Parameters
            }, TestContext.Current.CancellationToken);
            var options = RabbitMqSessionOptions.Parse(otherConnection);
            var ssh = SshConnectionOptions.Parse(options.SshConnection).Target;
            Assert.Equal(mqPassword, options.Value("password"));
            Assert.Equal(passphrase, ssh.Passphrase);
            Assert.Equal(prepared.Parameters["ssh.key"], ssh.KeyPath);
            char[] decoded = InstanceFileContent.Decode(ssh.KeyPath);
            try
            {
                using var restoredKey = RSA.Create();
                restoredKey.ImportFromEncryptedPem(decoded, ssh.Passphrase);
                Assert.Equal(key.ExportSubjectPublicKeyInfo(), restoredKey.ExportSubjectPublicKeyInfo());
            }
            finally { Array.Clear(decoded); }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>同步凭据验证不建立生产 MQ 会话。</summary>
    private sealed class NeverConnect : IRabbitMqSessionFactory
    {
        public Task<IRabbitMqSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
