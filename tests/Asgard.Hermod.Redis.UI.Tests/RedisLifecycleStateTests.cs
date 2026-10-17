namespace Asgard.Hermod.Redis.Tests;

/// <summary>实例重连保留键身份和草稿，不自动读取值或重放写入。</summary>
public sealed class RedisLifecycleStateTests
{
    [Fact]
    public async Task DisconnectAndReconnect_KeepDirtyKeyAndOriginalDatabase()
    {
        var session = new RedisTestSession(); var configs = new RedisTestConfigurations();
        await using var page = new RedisWorkspaceViewModel(new(configs.CurrentInstance), configs, session, configs, new RedisTestDialogs());
        await page.InitializeAsync(TestContext.Current.CancellationToken); page.DatabaseText = "4"; await page.QueryAsync();
        await page.OpenAsync(RedisKeyId.FromText("cache:key")); var tab = page.ActiveTab!;
        await tab.EditCommand.ExecuteAsync(); tab.EditorText = "kept draft"; int reads = session.Reads;
        session.Disconnect(); Assert.False(page.IsConnected); Assert.False(tab.CanSave); Assert.True(page.Connection.IsDisconnected); Assert.False(page.Connection.IsOpen);
        page.Connection.DismissCommand.Execute(null); Assert.True(page.ReconnectCommand.CanExecute(null));
        await page.ReconnectCommand.ExecuteAsync();
        Assert.True(page.IsConnected); Assert.True(page.HasConnectedOnce); Assert.Same(tab, page.ActiveTab);
        Assert.Equal(4, tab.Database); Assert.Equal("4", page.DatabaseText); Assert.True(tab.IsDirty); Assert.True(tab.CanSave);
        Assert.Equal("kept draft", tab.EditorText); Assert.Equal(reads, session.Reads); Assert.Equal(0, session.Saves);
        Assert.Null(page.InitialConnectionFailure);
    }
}
