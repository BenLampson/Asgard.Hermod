namespace Asgard.Hermod.Sync.Tests;

/// <summary>实际 HTTP 和 SQLite 明细版本、凭据加密以及旧快照迁移。</summary>
public sealed class ConfigurationApiTests
{
    private const string Password = "configuration-password";
    private static WorkspaceSnapshot Snapshot(string name = "Instance") => new(1,
        [new("one", name, "storage", "provider", new Dictionary<string, string>
        { ["endpoint"] = "https://endpoint.test", ["accessKeyId"] = "private-key-id", ["secret"] = "private-secret", ["unknown"] = "unknown-secret" }, DateTime.UnixEpoch, [])], []);
    /// <summary>普通字段可读，所有秘密及未知字段加密，条目版本由服务器生成。</summary>
    [Fact]
    public async Task Details_AreReadableWhileCredentialsStayEncrypted()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync(); host.SignIn(host.Token());
        var codec = new ConfigurationCodec(new ConfigurationTestRegistry()); var snapshot = Snapshot();
        var encoded = codec.Encode(snapshot, new(1, [], []), new("0", null, [], [], null), Password, "owner");
        Assert.Equal("https://endpoint.test", Assert.Single(encoded.Instances).Parameters["endpoint"]);
        Assert.Single(encoded.Instances[0].Parameters);
        string json = JsonSerializer.Serialize(encoded); Assert.DoesNotContain("private-secret", json); Assert.DoesNotContain("private-key-id", json); Assert.DoesNotContain("unknown-secret", json);
        var saved = await PutAsync(host, "0", encoded);
        Assert.NotEqual("0", saved.Instances[0].Version); Assert.NotEqual("0", saved.Revision);
        var restored = codec.Decode(saved, Password, "owner"); Assert.True(WorkspaceContent.Equal(snapshot, restored));
        Assert.Throws<InvalidOperationException>(() => codec.Decode(saved, "incorrect-password", "owner"));
        Assert.Throws<InvalidOperationException>(() => codec.Decode(saved, Password, "another-owner"));
        var unchanged = codec.Encode(restored, restored, saved, Password, "owner");
        var resaved = await PutAsync(host, saved.Revision, unchanged); Assert.Equal(saved.Instances[0].Version, resaved.Instances[0].Version);
        host.SignIn(host.Token(subject: "other")); var other = await GetAsync(host); Assert.Empty(other.Instances); Assert.Equal("0", other.Revision);
    }
    /// <summary>集合或条目过期时整体失败，成功更新只改变修改过的条目版本。</summary>
    [Fact]
    public async Task StaleVersions_RejectEntireUpload()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync(); host.SignIn(host.Token());
        var codec = new ConfigurationCodec(new ConfigurationTestRegistry()); var snapshot = Snapshot();
        var saved = await PutAsync(host, "0", codec.Encode(snapshot, new(1, [], []), new("0", null, [], [], null), Password, "owner"));
        using var staleCollection = await host.Client.PutAsJsonAsync("/api/sync/configuration", new ConfigurationUpload("0", saved), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, staleCollection.StatusCode);
        var wrongEntry = saved with { Instances = [saved.Instances[0] with { Version = "wrong", Name = "Must not save" }] };
        using var staleEntry = await host.Client.PutAsJsonAsync("/api/sync/configuration", new ConfigurationUpload(saved.Revision, wrongEntry), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, staleEntry.StatusCode); Assert.Equal("Instance", (await GetAsync(host)).Instances[0].Name);
        var changed = codec.Encode(Snapshot("Updated"), snapshot, saved, Password, "owner"); var updated = await PutAsync(host, saved.Revision, changed);
        Assert.NotEqual(saved.Instances[0].Version, updated.Instances[0].Version);
        var differentProtection = updated with { Protection = WorkspaceEncryption.Encrypt(new(1, [], []), "new-password-1234", "owner:configuration") };
        using var reset = await host.Client.PutAsJsonAsync("/api/sync/configuration", new ConfigurationUpload(updated.Revision, differentProtection), TestContext.Current.CancellationToken);
        Assert.NotEqual(HttpStatusCode.OK, reset.StatusCode);
    }
    /// <summary>旧整包只能由客户端解密迁移，迁移后旧上传被拒绝。</summary>
    [Fact]
    public async Task LegacySnapshot_MigratesWithoutLosingData()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync(); host.SignIn(host.Token());
        var legacy = WorkspaceEncryption.Encrypt(Snapshot(), Password, "owner");
        using var oldUpload = await host.Client.PutAsJsonAsync("/api/sync/workspace", new WorkspaceUpload("0", legacy), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, oldUpload.StatusCode);
        var before = await GetAsync(host); Assert.NotNull(before.LegacyWorkspace);
        var codec = new ConfigurationCodec(new ConfigurationTestRegistry()); var decoded = codec.Decode(before, Password, "owner");
        var migrated = await PutAsync(host, before.Revision, codec.Encode(decoded, decoded, before, Password, "owner"));
        Assert.Null(migrated.LegacyWorkspace); Assert.True(WorkspaceContent.Equal(decoded, codec.Decode(migrated, Password, "owner")));
        using var oldAgain = await host.Client.PutAsJsonAsync("/api/sync/workspace", new WorkspaceUpload("0", legacy), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, oldAgain.StatusCode);
    }
    /// <summary>服务门禁同样保护明细接口。</summary>
    [Fact]
    public async Task DetailApi_RequiresValidUserAndOnlinePermission()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync();
        using var anonymous = await host.Client.GetAsync("/api/sync/configuration", TestContext.Current.CancellationToken); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        host.SignIn(host.Token(type: "BackendService")); using var service = await host.Client.GetAsync("/api/sync/configuration", TestContext.Current.CancellationToken); Assert.Equal(HttpStatusCode.Forbidden, service.StatusCode);
    }
    /// <summary>只从成功响应包络读取数据。</summary>
    private static async Task<CloudConfiguration> PutAsync(ApiTestHost host, string revision, CloudConfiguration configuration)
    {
        using var response = await host.Client.PutAsJsonAsync("/api/sync/configuration", new ConfigurationUpload(revision, configuration), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return json.GetProperty("data").Deserialize<CloudConfiguration>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private static async Task<CloudConfiguration> GetAsync(ApiTestHost host)
    {
        var json = await host.Client.GetFromJsonAsync<JsonElement>("/api/sync/configuration", TestContext.Current.CancellationToken);
        return json.GetProperty("data").Deserialize<CloudConfiguration>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
