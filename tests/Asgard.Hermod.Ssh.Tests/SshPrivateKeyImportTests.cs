using System.Security.Cryptography;
using System.Text;
using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.Ssh;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>生成一次性测试私钥，验证删除原文件后仍保留可移植的认证材料。</summary>
public sealed class SshPrivateKeyImportTests
{
    /// <summary>目标和跳板独立导入，保存内容与原设备路径脱钩。</summary>
    [Theory]
    [InlineData(SshIds.Direct)]
    [InlineData(SshIds.Jump)]
    [InlineData(SshIds.Proxy)]
    public async Task ImportedKeys_SurviveSourceDeletionAndRepeatPreparation(string route)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pem");
        using var key = RSA.Create(2048); string pem = key.ExportPkcs8PrivateKeyPem();
        await File.WriteAllTextAsync(path, pem, TestContext.Current.CancellationToken);
        try
        {
            var values = new Dictionary<string, string>
            {
                ["host"] = "test.example",
                ["user"] = "tester",
                ["auth"] = SshIds.PrivateKey,
                ["key"] = path,
                ["jump.host"] = "jump.example",
                ["jump.user"] = "jumper",
                ["jump.auth"] = SshIds.PrivateKey,
                ["jump.key"] = path,
                ["proxy.host"] = "proxy.example"
            };
            var provider = new SshInstanceProvider(route, new NeverConnect());
            var prepared = await provider.PrepareConnectionAsync(new() { TypeId = SshIds.Type, ProviderId = route, Parameters = values }, TestContext.Current.CancellationToken);
            File.Delete(path);
            Assert.DoesNotContain(path, prepared.Parameters["key"]);
            char[] decoded = InstanceFileContent.Decode(prepared.Parameters["key"]);
            try { Assert.Equal(pem, new string(decoded)); } finally { Array.Clear(decoded); }
            var repeated = await provider.PrepareConnectionAsync(prepared, TestContext.Current.CancellationToken);
            Assert.Equal(prepared.Parameters["key"], repeated.Parameters["key"]);
            var profile = SshConnectionOptions.Parse(repeated);
            Assert.IsType<SshImportedKeyCredential>(SshPrivateKey.Credential(profile.Target));
            if (profile.JumpHost is { } jump) Assert.IsType<SshImportedKeyCredential>(SshPrivateKey.Credential(jump));
            Assert.DoesNotContain(pem, profile.ToString()); Assert.DoesNotContain(pem, profile.Target.ToString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    /// <summary>过大、损坏和非 UTF-8 内容均用资源错误反馈，不回显秘密。</summary>
    [Fact]
    public async Task Import_RejectsOversizedFilesAndInvalidEncodingSafely()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(path, new byte[InstanceFileContent.MaximumBytes + 1], TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SshPrivateKey.ImportAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal("SshKeyImportFailed", error.Data["Hermod.ResourceKey"]); Assert.DoesNotContain(path, error.Message);
            Assert.Throws<InvalidOperationException>(() => InstanceFileContent.Decode(InstanceFileContent.Encode([0xff])));
            Assert.Throws<InvalidOperationException>(() => InstanceFileContent.Decode("hermod-secret-file:v1:broken"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private sealed class NeverConnect : ISshConnectionFactory
    {
        public Task<ISshConnection> OpenAsync(InstanceConnectionOptions options, int columns, int rows, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
