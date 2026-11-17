using System.Text.Json;
using Asgard.Hermod.ObjectStorage.Abstractions.Clients;
using Asgard.Hermod.ObjectStorage.Abstractions.Models;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpAccessTests
{
    [Fact]
    public async Task DisabledAccessCannotConnectAndReenablingReplacesTheClientSecret()
    {
        using var rig = new McpTestRig([]);
        Assert.Null(rig.Access.Connect());
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var old = rig.Session;
        using var config = JsonDocument.Parse(await rig.Access.GetClientConfigurationAsync("", McpTestRig.Token));
        var launcher = config.RootElement.GetProperty("mcpServers").GetProperty("hermod");
        Assert.Equal("Hermod.Mcp.exe", launcher.GetProperty("command").GetString());
        Assert.True(old.Policy.Secret == launcher.GetProperty("env").GetProperty("HERMOD_MCP_TOKEN").GetString());
        await rig.Access.SetEnabledAsync("", false, McpTestRig.Token);
        Assert.True(old.Revoked.IsCancellationRequested);
        Assert.Null(rig.Access.Connect());
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        Assert.False(old.Policy.Secret == rig.Session.Policy.Secret);
    }

    [Fact]
    public async Task DiscoveryHidesUnsharedConnectionsAndOldGrantsCannotOpenAChangedTarget()
    {
        var shared = new McpTarget("shared", "exports", "storage", "1");
        var secret = new McpTarget("secret", "private instance", "mysql", "2");
        using var rig = new McpTestRig([shared, secret]);
        var grant = await rig.GrantAsync(shared, "exports", "nightly/");
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var session = rig.Session;
        var discovery = await rig.Tools.InvokeAsync(session, "hermod_list_instances", null, rig.Cursors, McpTestRig.Token);
        var item = Assert.Single(discovery.StructuredContent!.Value.GetProperty("instances").EnumerateArray());
        Assert.Equal(shared.Id, item.GetProperty("instance_id").GetString());
        Assert.DoesNotContain("private instance", item.GetRawText());
        Assert.DoesNotContain("kept-in-hermod", item.GetRawText());

        rig.Store.Targets = [shared with { Revision = "retargeted" }, secret];
        Assert.Single(rig.Tools.List(session));
        var denied = await rig.CallAsync("hermod_storage_list", grant);
        Assert.True(denied.IsError);
        Assert.Equal(0, rig.Store.Opened);

        rig.Store.Owner = "other-workspace";
        Assert.Null(rig.Access.Connect());
        var stale = await rig.Tools.InvokeAsync(session, "hermod_list_instances", null, rig.Cursors, McpTestRig.Token);
        Assert.True(stale.IsError);
        Assert.Null(stale.StructuredContent);
    }

    [Fact]
    public async Task RevocationDiscardsResultsEvenWhenTheProviderIgnoresCancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateResult = new TaskCompletionSource<ObjectStoragePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = ProtocolStub<IObjectStorageClient>.For((name, _) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.ListObjectsAsync), name);
            started.SetResult();
            return lateResult.Task;
        });
        var target = new McpTarget("exports", "exports", "storage", "1");
        using var rig = new McpTestRig([target], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client));
        var grant = await rig.GrantAsync(target, "exports", "nightly/");
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        Task<CallToolResult> pending = rig.CallAsync("hermod_storage_list", grant);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), McpTestRig.Token);
        await rig.Access.RemoveGrantAsync("", grant.Id, McpTestRig.Token);
        lateResult.SetResult(new([new("nightly/private.csv", false)]));
        var result = await pending;
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.DoesNotContain("private.csv", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }
}
