namespace Asgard.Hermod.UI.Tests;

/// <summary>验证真实 SQLite 的设备级信任持久化、主机归一化与并发覆盖保护。</summary>
public sealed class SshHostKeyTrustTests
{
    /// <summary>记录中的主机与数据库身份边界不符时失败，不能挪用另一主机的信任。</summary>
    [Fact]
    public async Task MismatchedStoredIdentityFailsSafely()
    {
        using var environment = new LocalStorageTestEnvironment(); using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var store = services.GetRequiredService<IHostKeyTrustStore>(); var original = Key("example.com", 22, 'A');
        Assert.True(await store.TrustAsync(original, null, TestContext.Current.CancellationToken));
        services.GetRequiredService<IFreeSql>().Ado.ExecuteNonQuery("UPDATE app_setting SET value = @json WHERE id LIKE 'ssh.host.%';",
            new { json = System.Text.Json.JsonSerializer.Serialize(original with { Host = "other.example" }) });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync("example.com", 22, TestContext.Current.CancellationToken));
        Assert.Equal("SshTrustUnavailable", error.Data["Hermod.ResourceKey"]);
    }
    /// <summary>设备设置在重建容器后仍存在，同一域名的不同端口独立。</summary>
    [Fact]
    public async Task HostTrustPersistsLocallyAndNormalizesIdentity()
    {
        using var environment = new LocalStorageTestEnvironment();
        var key = Key("EXAMPLE.COM.", 22, 'A');
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<IHostKeyTrustStore>();
            Assert.True(await store.TrustAsync(key, null, TestContext.Current.CancellationToken));
            Assert.Equal(key, await store.LoadAsync("example.com", 22, TestContext.Current.CancellationToken));
            Assert.Null(await store.LoadAsync("example.com", 2222, TestContext.Current.CancellationToken));
        }
        using var reopened = environment.CreateServices();
        Assert.Equal(key, await reopened.GetRequiredService<IHostKeyTrustStore>().LoadAsync("example.com", 22, TestContext.Current.CancellationToken));
    }

    /// <summary>指纹变化只能基于用户看到的旧指纹替换，迟到确认不能覆盖新记录。</summary>
    [Fact]
    public async Task CompareAndSwapPreventsStaleTrustConfirmation()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var store = services.GetRequiredService<IHostKeyTrustStore>();
        var original = Key("::1", 22, 'A'); var changed = Key("::1", 22, 'B');
        Assert.True(await store.TrustAsync(original, null, TestContext.Current.CancellationToken));
        Assert.False(await store.TrustAsync(changed, null, TestContext.Current.CancellationToken));
        Assert.True(await store.TrustAsync(changed, original.Fingerprint, TestContext.Current.CancellationToken));
        Assert.False(await store.TrustAsync(original, original.Fingerprint, TestContext.Current.CancellationToken));
        Assert.Equal(changed, await store.LoadAsync("0:0:0:0:0:0:0:1", 22, TestContext.Current.CancellationToken));
    }

    /// <summary>非法公开身份拒绝落库，不改变已有信任。</summary>
    [Fact]
    public async Task InvalidFingerprintsAreRejected()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var store = services.GetRequiredService<IHostKeyTrustStore>();
        await Assert.ThrowsAnyAsync<Exception>(() => store.TrustAsync(Key("example.com", 22, 'A') with { Fingerprint = "response password=secret" }, null, TestContext.Current.CancellationToken));
        Assert.Null(await store.LoadAsync("example.com", 22, TestContext.Current.CancellationToken));
    }

    private static TrustedHostKey Key(string host, int port, char fingerprint) => new(host, port, "ssh-ed25519", "SHA256:" + new string(fingerprint, 43));
}
