namespace Asgard.Hermod.Sync.Tests;

/// <summary>模型凭据独立用途加密、旧设备兼容和连接整体合并契约。</summary>
public sealed class AgentModelSyncTests
{
    private const string Password = "model-sync-password";
    private static readonly AgentModelProfile Profile = new("11111111111111111111111111111111", "Private profile", "https://private-model.example/v1", "private-model", "private-api-key", DateTimeOffset.UnixEpoch);
    private static WorkspaceSnapshot Snapshot(params AgentModelProfile[] profiles) => new(1, [], [])
    { AgentModels = new(profiles, profiles.FirstOrDefault()?.Id ?? "") };

    /// <summary>模型配置所有字段均为密文，未修改时复用信封，删除产生显式空集合。</summary>
    [Fact]
    public void Codec_EncryptsAllFieldsAndExplicitlyClearsModels()
    {
        var codec = new ConfigurationCodec(new ConfigurationTestRegistry());
        var original = Snapshot(Profile);
        var encoded = codec.Encode(original, Snapshot(), new("0", null, [], [], null), Password, "owner");
        Assert.NotNull(encoded.AgentModels);
        string wire = JsonSerializer.Serialize(encoded);
        foreach (string secret in new[] { Profile.Name, Profile.Endpoint, Profile.Model, Profile.ApiKey }) Assert.DoesNotContain(secret, wire);
        Assert.True(WorkspaceContent.Equal(original, codec.Decode(encoded, Password, "owner")));
        Assert.Equal(encoded.AgentModels, codec.Encode(original, original, encoded, Password, "owner").AgentModels);
        var removed = codec.Encode(Snapshot(), original, encoded, Password, "owner");
        Assert.NotNull(removed.AgentModels); Assert.NotEqual(encoded.AgentModels, removed.AgentModels);
        Assert.Empty(codec.Decode(removed, Password, "owner").AgentModels.Profiles);
        Assert.Null(codec.Encode(Snapshot(), Snapshot(), new("0", null, [], [], null), Password, "owner").AgentModels);
    }

    /// <summary>密码校验包不能冒充模型包；账号、盐或内容变化均不能解密。</summary>
    [Fact]
    public void Codec_RejectsPurposeOwnerSaltAndCiphertextChanges()
    {
        var codec = new ConfigurationCodec(new ConfigurationTestRegistry());
        var cloud = codec.Encode(Snapshot(Profile), Snapshot(), new("0", null, [], [], null), Password, "owner");
        Assert.Throws<InvalidOperationException>(() => codec.Decode(cloud with { AgentModels = cloud.Protection }, Password, "owner"));
        Assert.Throws<InvalidOperationException>(() => codec.Decode(cloud, Password, "other-owner"));
        var envelope = cloud.AgentModels!;
        byte[] ciphertext = Convert.FromBase64String(envelope.Ciphertext); ciphertext[0] ^= 1;
        Assert.Throws<InvalidOperationException>(() => codec.Decode(cloud with { AgentModels = envelope with { Ciphertext = Convert.ToBase64String(ciphertext) } }, Password, "owner"));
        Assert.Throws<InvalidOperationException>(() => codec.Decode(cloud with { AgentModels = envelope with { Salt = Convert.ToBase64String(new byte[16]) } }, Password, "owner"));
    }

    /// <summary>独立重命名可以合并，冲突连接必须整体选择，预览不泄露密钥。</summary>
    [Fact]
    public void Merge_TreatsEndpointModelAndKeyAsOneConnection()
    {
        var merge = new WorkspaceMerge();
        var local = Profile with { Name = "Renamed", ApiKey = "local-key" };
        var remote = Profile with { Endpoint = "https://other.example/v1", Model = "other-model", ApiKey = "remote-key" };
        merge.Merge(Snapshot(Profile), Snapshot(local), Snapshot(remote));
        var conflict = Assert.Single(merge.Conflicts);
        Assert.DoesNotContain("local-key", conflict.LocalValue); Assert.DoesNotContain("remote-key", conflict.CloudValue);
        var result = merge.Merge(Snapshot(Profile), Snapshot(local), Snapshot(remote), new Dictionary<string, bool> { [conflict.Key] = true });
        var selected = Assert.Single(result.AgentModels.Profiles);
        Assert.Equal("Renamed", selected.Name); Assert.Equal(remote.Endpoint, selected.Endpoint);
        Assert.Equal(remote.Model, selected.Model); Assert.Equal(remote.ApiKey, selected.ApiKey); Assert.Empty(merge.Conflicts);
    }

    /// <summary>删除默认配置撤销引用，不复活另一设备保留的旧默认。</summary>
    [Fact]
    public void Merge_DeletingDefaultRemovesReference()
    {
        var merge = new WorkspaceMerge();
        var result = merge.Merge(Snapshot(Profile), Snapshot(), Snapshot(Profile));
        Assert.Empty(result.AgentModels.Profiles); Assert.Empty(result.AgentModels.DefaultProfileId); Assert.Empty(merge.Conflicts);
    }

    /// <summary>旧客户端省略字段保留服务器密文，现代客户端显式清除；账号隔离保持有效。</summary>
    [Fact]
    public async Task Api_PreservesLegacyOmissionAndAcceptsEncryptedClear()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync(); host.SignIn(host.Token());
        var codec = new ConfigurationCodec(new ConfigurationTestRegistry());
        var saved = await PutAsync(host, "0", codec.Encode(Snapshot(Profile), Snapshot(), new("0", null, [], [], null), Password, "owner"));
        var legacy = await PutAsync(host, saved.Revision, saved with { AgentModels = null });
        Assert.Equal(saved.AgentModels, legacy.AgentModels);
        Assert.True(WorkspaceContent.Equal(Snapshot(Profile), codec.Decode(legacy, Password, "owner")));
        var cleared = await PutAsync(host, legacy.Revision, codec.Encode(Snapshot(), Snapshot(Profile), legacy, Password, "owner"));
        Assert.NotNull(cleared.AgentModels); Assert.Empty(codec.Decode(cleared, Password, "owner").AgentModels.Profiles);
        host.SignIn(host.Token(subject: "another"));
        using var response = await host.Client.GetAsync("/api/sync/configuration", TestContext.Current.CancellationToken);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("data").GetProperty("agentModels").ValueKind);
    }

    private static async Task<CloudConfiguration> PutAsync(ApiTestHost host, string revision, CloudConfiguration value)
    {
        using var response = await host.Client.PutAsJsonAsync("/api/sync/configuration", new ConfigurationUpload(revision, value), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return json.GetProperty("data").Deserialize<CloudConfiguration>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
