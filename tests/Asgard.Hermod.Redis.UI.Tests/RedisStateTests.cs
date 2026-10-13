using System.Globalization;
using System.Text;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>核心交互验证实际用户状态，不重复实现细节。</summary>
public sealed class RedisStateTests
{
    [Fact]
    public async Task BinaryString_HexAndBase64PreserveBytesWithoutLossyText()
    {
        var session = new RedisTestSession { Snapshot = new("string", 3, -1, [new([], [255, 0, 1])], true) };
        using var tab = new RedisKeyTabViewModel(session, 0, RedisKeyId.FromText("binary"), new RedisTestDialogs());
        await tab.ReadAsync(); Assert.Equal(new[] { "hex", "base64" }, tab.Formats.Select(format => format.Id));
        await tab.EditCommand.ExecuteAsync(); tab.Format = "hex";
        Assert.Equal(new byte[] { 255, 0, 1 }, RedisValueCodec.Decode(tab.EditorText, "hex"));
        tab.EditorText = "DEADBEEF"; await tab.SaveCommand.ExecuteAsync();
        Assert.Equal(new byte[] { 222, 173, 190, 239 }, Assert.Single(session.Snapshot.Entries).Value);
    }
    [Fact]
    public async Task Tree_LoadsOnlyOpenedPrefixesAndKeepsAnEmptyBatchCursor()
    {
        var session = new RedisTestSession
        {
            Scan = (_, cursor, pattern) => Task.FromResult(pattern == "*" ? cursor == "0" ? new RedisScanPage("17", [])
                : new("0", [RedisKeyId.FromText("user:1042"), RedisKeyId.FromText("user:1042:profile")])
                : new("0", [RedisKeyId.FromText("user:1042"), RedisKeyId.FromText("user:1042:profile")]))
        };
        using var tree = new RedisKeyTree(session, 0, "*", "");
        await tree.LoadAsync(tree.Root); Assert.True(tree.Root.HasMore); Assert.Empty(tree.Root.Children); Assert.Equal(0, session.Reads);
        await tree.LoadAsync(tree.Root); Assert.False(tree.Root.HasMore);
        var user = Assert.Single(tree.Root.Children); Assert.Equal("user:", user.Prefix); Assert.Equal(2, session.Calls.Count);
        await tree.LoadAsync(user);
        Assert.Contains(user.Children, node => node.Key is not null && node.Key.DisplayName == "user:1042");
        Assert.Contains(user.Children, node => node.IsGroup && node.Prefix == "user:1042:");
        Assert.Equal(0, session.Reads);
    }

    [Fact]
    public async Task Tree_EscapesLiteralPrefixesAndAppliesUserGlobSeparately()
    {
        var session = new RedisTestSession { Scan = (_, _, _) => Task.FromResult(new RedisScanPage("0", [RedisKeyId.FromText("a[*]:keep"), RedisKeyId.FromText("a[*]:drop")])) };
        using var tree = new RedisKeyTree(session, 0, "*:keep", ""); await tree.LoadAsync(tree.Root);
        var group = Assert.Single(tree.Root.Children); await tree.LoadAsync(group);
        Assert.Equal("a\\[\\*\\]:*", session.Calls[1].Pattern);
        Assert.Equal("keep", Assert.Single(group.Children).Label);
    }

    [Fact]
    public async Task Tree_IgnoresLateResultsWhenLeavingScope()
    {
        var pending = new TaskCompletionSource<RedisScanPage>(); var session = new RedisTestSession { Scan = (_, _, _) => pending.Task };
        using var tree = new RedisKeyTree(session, 0, "*", ""); var read = tree.LoadAsync(tree.Root);
        tree.Suspend(); pending.SetResult(new("0", [RedisKeyId.FromText("late:key")])); await read;
        Assert.Empty(tree.Root.Children); Assert.True(tree.Root.HasMore);
    }

    [Fact]
    public async Task Tree_RetriesFailedPrimaryWithoutRewindingSuccessfulPrimary()
    {
        bool failed = true; var session = new RedisTestSession
        {
            ScanNodes = ["one", "two"],
            Scan = (node, _, _) =>
            node == "two" && failed ? Task.FromException<RedisScanPage>(new RedisException("RedisConnectionFailed", "REDIS_CONNECTION"))
            : Task.FromResult(new RedisScanPage(node == "one" ? "5" : "0", []))
        };
        using var tree = new RedisKeyTree(session, 0, "*", ""); await tree.LoadAsync(tree.Root); failed = false;
        await tree.LoadAsync(tree.Root); Assert.Equal("two", session.Calls.Last().Node);
        Assert.Equal(1, session.Calls.Count(call => call.Node == "one"));
        await tree.LoadAsync(tree.Root); Assert.Equal("5", session.Calls.Last().Cursor);
    }

    [Fact]
    public async Task Draft_SurvivesConflictAndCloseCancellation()
    {
        var session = new RedisTestSession { SaveError = new RedisException("RedisConflict", "REDIS_CONFLICT") };
        var dialogs = new RedisTestDialogs(); using var tab = new RedisKeyTabViewModel(session, 0, RedisKeyId.FromText("user:1042:profile"), dialogs);
        await tab.ReadAsync(); await tab.EditCommand.ExecuteAsync(); Assert.False(tab.IsDirty);
        tab.EditorText = "draft"; Assert.True(tab.IsDirty); Assert.False(await tab.SaveAsync()); Assert.Equal("draft", tab.EditorText);
        Assert.False(await tab.RequestCloseAsync()); Assert.True(tab.IsDirty);
        await tab.RefreshCommand.ExecuteAsync(); Assert.Equal(1, dialogs.Comparisons); Assert.Equal("draft", tab.EditorText);
        dialogs.Choice = RedisCloseChoice.Save; Assert.False(await tab.RequestCloseAsync());
        session.SaveError = null; Assert.True(await tab.RequestCloseAsync()); Assert.False(tab.IsDirty);
    }

    [Fact]
    public async Task UnknownWrite_BlocksReplayAndReadCanReconcileIdenticalValue()
    {
        var session = new RedisTestSession { SaveError = new RedisException("RedisWriteUnknown", "REDIS_WRITE_UNKNOWN") };
        using var tab = new RedisKeyTabViewModel(session, 0, RedisKeyId.FromText("key"), new RedisTestDialogs());
        await tab.ReadAsync(); await tab.EditCommand.ExecuteAsync(); tab.EditorText = "draft"; await tab.SaveAsync();
        Assert.False(tab.CanSave); Assert.True(tab.IsDirty); await tab.SaveAsync(); Assert.Equal(1, session.Saves);
        session.Snapshot = session.Snapshot with { Entries = [new([], "draft"u8.ToArray())] };
        await tab.RefreshCommand.ExecuteAsync(); Assert.False(tab.IsDirty); Assert.Equal("draft", tab.EditorText);
    }

    [Fact]
    public async Task Workspace_ReusesRawKeyTabsKeepsDraftsAcrossDatabaseChangesAndChoosesAdjacentOnClose()
    {
        var session = new RedisTestSession(); var configs = new RedisTestConfigurations(); var dialogs = new RedisTestDialogs { Choice = RedisCloseChoice.Discard };
        await using var workspace = new RedisWorkspaceViewModel(new(RedisTestConfigurations.Instance), configs, session, configs, dialogs);
        await workspace.InitializeAsync(TestContext.Current.CancellationToken);
        var first = RedisKeyId.FromText("same:key"); await workspace.OpenAsync(first); var tab = workspace.ActiveTab!;
        await tab.EditCommand.ExecuteAsync(); tab.EditorText = "draft";
        await workspace.OpenAsync(RedisKeyId.FromText("another:key")); await workspace.OpenAsync(first);
        Assert.Equal(2, workspace.Tabs.Count); Assert.Same(tab, workspace.ActiveTab); Assert.Equal("draft", tab.EditorText);
        workspace.DatabaseText = "1"; await workspace.QueryAsync(); await workspace.OpenAsync(first);
        Assert.Equal(3, workspace.Tabs.Count); Assert.Equal(1, workspace.ActiveTab!.Database); Assert.True(tab.IsDirty);
        await workspace.CloseTabCommand.ExecuteAsync(workspace.ActiveTab); Assert.Equal(2, workspace.Tabs.Count); Assert.Equal("another:key", workspace.ActiveTab!.FullName);
        workspace.ActivateTabCommand.Execute(tab); Assert.True(await workspace.RequestCloseAsync());
        await workspace.CloseTabCommand.ExecuteAsync(tab); Assert.Single(workspace.Tabs); Assert.Equal("another:key", workspace.ActiveTab!.FullName);
        await workspace.CloseTabCommand.ExecuteAsync(workspace.ActiveTab); Assert.True(workspace.IsEmpty); Assert.NotNull(workspace.Tree);
    }

    [Theory]
    [InlineData("short:key", "short:key")]
    [InlineData("user:1042:profile", "user:1042:p…")]
    [InlineData("😀😀😀😀😀😀😀😀😀😀😀😀😀", "😀😀😀😀😀😀😀😀😀😀😀…")]
    [InlineData("ééééééééééééé", "ééééééééééé…")]
    public void Headers_CountTextElementsIncludingEllipsis(string full, string expected)
    { Assert.Equal(expected, RedisValueCodec.TabHeader(full)); Assert.True(StringInfo.ParseCombiningCharacters(RedisValueCodec.TabHeader(full)).Length <= 12); }

    [Fact]
    public void Codecs_DoNotReplaceInvalidUtf8OrSaveJsonFormattingImplicitly()
    {
        byte[] binary = [0xff, 0, 0x80]; Assert.False(RedisValueCodec.IsUtf8(binary));
        Assert.Equal(binary, RedisValueCodec.Decode(RedisValueCodec.Encode(binary, "base64"), "base64"));
        Assert.Throws<InvalidOperationException>(() => RedisValueCodec.Encode(binary, "text"));
        byte[] json = Encoding.UTF8.GetBytes("{ \"a\":1 }"); Assert.Equal("{ \"a\":1 }", RedisValueCodec.Encode(json, "json"));
        Assert.NotEqual("{ \"a\":1 }", RedisValueCodec.Encode(json, "json", true));
    }
}
