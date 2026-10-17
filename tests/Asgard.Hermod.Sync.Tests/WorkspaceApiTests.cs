namespace Asgard.Hermod.Sync.Tests;

/// <summary>验证真实 HTTP 身份边界、数据归属、并发和安全失败行为。</summary>
public sealed class WorkspaceApiTests
{
    /// <summary>登录后 me 返回 JWT 用户名，密文按用户隔离，旧设备不能覆盖新版本。</summary>
    [Fact]
    public async Task Api_IsolatesUsersAndRejectsConflictingUpload()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync(); host.SignIn(host.Token());
        var me = await host.Client.GetFromJsonAsync<JsonElement>("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal("login-name", me.GetProperty("data").GetProperty("username").GetString());
        var encrypted = WorkspaceEncryption.Encrypt(new(1, [], []), "password-for-sync", "owner");
        using var upload = await host.Client.PutAsJsonAsync("/api/sync/workspace", new WorkspaceUpload("0", encrypted), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var data = (await upload.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("data");
        string revision = data.GetProperty("revision").GetString()!;
        Assert.NotEqual("0", revision);
        using var conflict = await host.Client.PutAsJsonAsync("/api/sync/workspace", new WorkspaceUpload("0", encrypted), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        host.SignIn(host.Token(subject: "user-b"));
        var other = await host.Client.GetFromJsonAsync<JsonElement>("/api/sync/workspace", TestContext.Current.CancellationToken);
        Assert.Equal("0", other.GetProperty("data").GetProperty("revision").GetString());
        host.SignIn(host.Token(username: "renamed-user"));
        var original = await host.Client.GetFromJsonAsync<JsonElement>("/api/sync/workspace", TestContext.Current.CancellationToken);
        Assert.Equal(revision, original.GetProperty("data").GetProperty("revision").GetString());
    }
    /// <summary>认证及身份边界失配均拒绝同步入口。</summary>
    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("expired", 401)]
    [InlineData("audience", 401)]
    [InlineData("tenant", 403)]
    [InlineData("application", 403)]
    [InlineData("client", 403)]
    [InlineData("service", 403)]
    public async Task Api_RejectsInvalidIdentity(string scenario, int status)
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync();
        if (scenario != "anonymous") host.SignIn(host.Token(tenant: scenario == "tenant" ? Guid.NewGuid().ToString() : null,
            application: scenario == "application" ? "other-app" : null, client: scenario == "client" ? "other-client" : null,
            type: scenario == "service" ? "BackendService" : "UserLogin", audience: scenario == "audience" ? "other-api" : "hermod-api", expired: scenario == "expired"));
        using var response = await host.Client.GetAsync("/api/sync/workspace", TestContext.Current.CancellationToken);
        Assert.Equal(status, (int)response.StatusCode);
    }
    /// <summary>停用用户和目录故障不能从 JWT 的旧权限获得继续访问。</summary>
    [Theory]
    [InlineData(false, 403)]
    [InlineData(true, 503)]
    public async Task Api_FailsClosedWhenDirectoryRejectsOrFails(bool unavailable, int status)
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync(); host.SignIn(host.Token());
        host.Verifier.Allowed = false; host.Verifier.Unavailable = unavailable;
        using var response = await host.Client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.DoesNotContain("resource_unavailable", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
    /// <summary>并行设备使用相同已知版本时仅一个写入成功。</summary>
    [Fact]
    public async Task Api_AllowsOnlyOneConcurrentWriterForSameRevision()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync(); host.SignIn(host.Token());
        var encrypted = WorkspaceEncryption.Encrypt(new(1, [], []), "password-for-sync", "owner");
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => host.Client.PutAsJsonAsync(
            "/api/sync/workspace", new WorkspaceUpload("0", encrypted), TestContext.Current.CancellationToken)));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
}
