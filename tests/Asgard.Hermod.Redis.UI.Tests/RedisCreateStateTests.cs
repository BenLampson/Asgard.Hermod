using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>新增草稿验证不覆盖、错误保留、编码无损及数据库数量标签。</summary>
public sealed class RedisCreateStateTests
{
    [Fact]
    public async Task ToolbarCreate_UsesCurrentDatabaseAndPrefixThenOpensAndRevealsCreatedKey()
    {
        var session = new RedisTestSession(); var configs = new RedisTestConfigurations(); var dialogs = new RedisTestDialogs();
        dialogs.CreateHandler = async (selectedSession, database, prefix, token) =>
        {
            Assert.Same(session, selectedSession); Assert.Equal(4, database); Assert.Equal("cache:", prefix);
            using var create = new RedisCreateViewModel(selectedSession, database, prefix, token) { Key = prefix + "created", EditorText = "initial value" };
            await create.SubmitCommand.ExecuteAsync(); return create.CreatedKey;
        };
        await using var workspace = new RedisWorkspaceViewModel(new(RedisTestConfigurations.Instance), configs, session, configs, dialogs);
        await workspace.InitializeAsync(TestContext.Current.CancellationToken); workspace.DatabaseText = "4"; workspace.Pattern = "other:*"; workspace.SelectedType = "list";
        await workspace.QueryAsync(); workspace.CreatePrefix = "cache:"; await workspace.NewKeyCommand.ExecuteAsync();
        Assert.Null(workspace.NewKeyCommand.LastException); Assert.Equal("cache:created", workspace.ActiveTab!.FullName); Assert.Equal(4, workspace.ActiveTab.Database);
        Assert.Equal("", workspace.Pattern); Assert.Equal("", workspace.SelectedType);
        var prefixNode = Assert.Single(workspace.Tree!.Root.Children); Assert.True(prefixNode.IsExpanded); Assert.Equal("cache:created", Assert.Single(prefixNode.Children).Key!.DisplayName);
    }
    [Theory]
    [InlineData("string")]
    [InlineData("hash")]
    [InlineData("list")]
    [InlineData("set")]
    [InlineData("zset")]
    [InlineData("stream")]
    public async Task Create_AllTypesKeepCorrectInitialDataAndDisableSecondSubmission(string type)
    {
        var session = new RedisTestSession(); using var model = new RedisCreateViewModel(session, 3, "cache:") { SelectedType = type, Key = "cache:new", EditorText = "raw value" };
        model.Rows[0].Name = "member"; model.Rows[0].Value = "row value"; model.Rows[0].Score = "1.5";
        await model.SubmitCommand.ExecuteAsync(); Assert.NotNull(model.CreatedKey); Assert.False(model.HasError); Assert.Equal(1, session.Saves);
        Assert.Equal(type, session.Snapshot.Type); var row = Assert.Single(session.Snapshot.Entries);
        Assert.Equal(type == "string" ? "raw value" : model.HasValue ? "row value" : "", System.Text.Encoding.UTF8.GetString(row.Value));
        await model.SubmitCommand.ExecuteAsync(); Assert.Equal(1, session.Saves);
    }
    [Theory]
    [InlineData("REDIS_KEY_EXISTS")]
    [InlineData("REDIS_PERMISSION")]
    [InlineData("REDIS_WRITE_UNKNOWN")]
    public async Task Create_FailureKeepsEveryInputAndUnknownWritesCannotReplay(string code)
    {
        var session = new RedisTestSession { SaveError = new RedisException(code == "REDIS_KEY_EXISTS" ? "RedisKeyAlreadyExists" : code == "REDIS_PERMISSION" ? "RedisPermissionDenied" : "RedisWriteUnknown", code) };
        using var model = new RedisCreateViewModel(session, 5) { Key = "kept:key", EditorText = "kept value", ExpiryMode = "seconds", ExpirySeconds = "80" };
        await model.SubmitCommand.ExecuteAsync(); Assert.Null(model.CreatedKey); Assert.True(model.HasError);
        Assert.Equal("kept:key", model.Key); Assert.Equal("kept value", model.EditorText); Assert.Equal("80", model.ExpirySeconds);
        Assert.Equal(code != "REDIS_WRITE_UNKNOWN", model.CanSubmit);
        session.SaveError = null; await model.SubmitCommand.ExecuteAsync(); Assert.Equal(code == "REDIS_WRITE_UNKNOWN" ? 1 : 2, session.Saves);
    }
    [Fact]
    public async Task Create_InvalidJsonDuplicateMembersAndExpiryNeverReachServer()
    {
        var session = new RedisTestSession(); using var model = new RedisCreateViewModel(session, 0) { Key = "new" };
        model.Format = "json"; model.EditorText = "{"; await model.SubmitCommand.ExecuteAsync(); Assert.Equal(0, session.Saves);
        Assert.Equal("{", model.EditorText); model.SelectedType = "set"; model.Rows[0].Name = "duplicate";
        model.AddRowCommand.Execute(null); model.Rows[1].Name = "duplicate"; await model.SubmitCommand.ExecuteAsync(); Assert.Equal(0, session.Saves);
        model.Rows[1].Name = "unique"; model.ExpiryMode = "seconds"; model.ExpirySeconds = "-1";
        await model.SubmitCommand.ExecuteAsync(); Assert.NotEmpty(model.ExpiryError); Assert.Equal(0, session.Saves);
    }
    [Fact]
    public async Task Create_BinaryStringRemainsByteExactAcrossHexAndBase64()
    {
        var session = new RedisTestSession(); using var model = new RedisCreateViewModel(session, 0) { Key = "binary" };
        model.Format = "hex"; model.EditorText = "FF0001"; model.Format = "base64";
        Assert.Equal("/wAB", model.EditorText); await model.SubmitCommand.ExecuteAsync(); Assert.Equal(new byte[] { 255, 0, 1 }, Assert.Single(session.Snapshot.Entries).Value);
    }
    [Fact]
    public async Task DatabaseCounts_RefreshStableOptionsAndNeverChangeSelection()
    {
        var session = new RedisTestSession { DatabaseKeyCount = database => Task.FromResult<long?>(database == 2 ? null : database * 100) };
        var configs = new RedisTestConfigurations(); await using var workspace = new RedisWorkspaceViewModel(new(RedisTestConfigurations.Instance), configs, session, configs, new RedisTestDialogs());
        await workspace.InitializeAsync(TestContext.Current.CancellationToken); workspace.DatabaseText = "5"; await workspace.QueryAsync();
        var options = workspace.Databases; var fifth = options[5]; int requests = session.Calls.Count;
        await workspace.RefreshDatabaseCountsAsync(); Assert.Same(options, workspace.Databases); Assert.Same(fifth, workspace.Databases[5]);
        Assert.Equal(Text.Format("RedisDatabaseWithCount", 5, "500"), fifth.Name); Assert.Equal("5", workspace.DatabaseText); Assert.Equal(requests, session.Calls.Count);
        Assert.Contains(Text.Get("RedisDatabaseCountUnknown"), options[2].Name);
    }
    [Fact]
    public async Task Reveal_CreatedKeyExpandsOnlyItsPathWithoutAdditionalScan()
    {
        var session = new RedisTestSession(); using var tree = new RedisKeyTree(session, 0, "*", ""); await tree.LoadAsync(tree.Root);
        int requests = session.Calls.Count; var key = RedisKeyId.FromText("user:1042:new"); tree.Reveal(key);
        var user = Assert.Single(tree.Root.Children); Assert.True(user.IsExpanded);
        var profile = Assert.Single(user.Children); Assert.True(profile.IsExpanded);
        Assert.Equal(key, Assert.Single(profile.Children).Key); Assert.Equal(requests, session.Calls.Count);
    }
}
