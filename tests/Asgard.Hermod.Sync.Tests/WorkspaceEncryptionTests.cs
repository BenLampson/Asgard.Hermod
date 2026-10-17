namespace Asgard.Hermod.Sync.Tests;

/// <summary>验证加密的身份绑定、篡改保护和跨设备可移植性。</summary>
public sealed class WorkspaceEncryptionTests
{
    /// <summary>相同密码能跨设备解密，每次上传的 salt 和 nonce 都独立。</summary>
    [Fact]
    public void EncryptedSnapshot_RoundTripsWithoutExposingSecrets()
    {
        var snapshot = new WorkspaceSnapshot(1, [new("instance", "name", "storage", "oss",
            new Dictionary<string, string> { ["secret"] = "credential-value" }, DateTime.UtcNow, [])], []);
        var first = WorkspaceEncryption.Encrypt(snapshot, "password-for-sync", "owner-a");
        var second = WorkspaceEncryption.Encrypt(snapshot, "password-for-sync", "owner-a");
        Assert.NotEqual(first.Salt, second.Salt); Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.DoesNotContain("credential-value", JsonSerializer.Serialize(first));
        Assert.Equal("credential-value", WorkspaceEncryption.Decrypt(first, "password-for-sync", "owner-a").Instances[0].Parameters["secret"]);
    }
    /// <summary>密码错误、账号改变和密文篡改都不能产生可用明文。</summary>
    [Theory]
    [InlineData("wrong-password", "owner-a", false)]
    [InlineData("password-for-sync", "owner-b", false)]
    [InlineData("password-for-sync", "owner-a", true)]
    public void Decryption_RejectsWrongPasswordOwnerAndTampering(string password, string owner, bool tamper)
    {
        var encrypted = WorkspaceEncryption.Encrypt(new(1, [], []), "password-for-sync", "owner-a");
        if (tamper)
        {
            byte[] ciphertext = Convert.FromBase64String(encrypted.Ciphertext); ciphertext[0] ^= 1;
            encrypted = encrypted with { Ciphertext = Convert.ToBase64String(ciphertext) };
        }
        var error = Assert.Throws<InvalidOperationException>(() => WorkspaceEncryption.Decrypt(encrypted, password, owner));
        Assert.Equal("SyncDecryptionFailed", error.Data["Hermod.ResourceKey"]);
    }
    /// <summary>未知格式或任意工作因子在执行密码派生之前拒绝。</summary>
    [Fact]
    public void Envelope_RejectsUnsupportedCostAndOversizedPayload()
    {
        var encrypted = WorkspaceEncryption.Encrypt(new(1, [], []), "password-for-sync", "owner-a");
        Assert.Throws<InvalidOperationException>(() => WorkspaceEncryption.ValidateEnvelope(encrypted with { Iterations = int.MaxValue }));
        Assert.Throws<InvalidOperationException>(() => WorkspaceEncryption.ValidateEnvelope(encrypted with { Ciphertext = new string('A', 8 * 1024 * 1024) }));
    }
    /// <summary>用户名展示取 JWT name，不能被邮箱或 sub 替代。</summary>
    [Fact]
    public void Account_ReadsUsernameFromNameClaim()
    {
        var claims = JsonSerializer.SerializeToElement(new { sub = "subject", tenant_id = "tenant", application_id = "app", name = "username", email = "email@example.com" });
        Assert.Equal("username", HeimdallAccountService.ReadAccount(claims).Username);
        Assert.Throws<InvalidOperationException>(() => HeimdallAccountService.ReadAccount(JsonSerializer.SerializeToElement(new { sub = "subject", tenant_id = "tenant", application_id = "app", email = "email@example.com" })));
    }
}
