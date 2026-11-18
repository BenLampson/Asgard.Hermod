using System.Text.Json;
using Asgard.Hermod.ObjectStorage.Abstractions.Clients;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpInstanceApprovalTests
{
    [Fact]
    public async Task AlwaysAllowSkipsOnlyTheMatchingInstanceAndRevisionAcrossConnections()
    {
        var storage = new McpTarget("oss", "Objects", "storage", "v1");
        var other = new McpTarget("other", "Other", "storage", "v1");
        var approval = new McpInstanceApproval(storage.Id, storage.TypeId, storage.Revision);
        var client = ProtocolStub<IObjectStorageClient>.For((name, _) => name == nameof(IObjectStorageClient.ObjectExistsAsync)
            ? Task.FromResult(false) : throw new InvalidOperationException(name));
        using var rig = new McpTestRig([storage, other], defaultAccess: true,
            storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client));
        var token = McpTestRig.Token;
        await rig.Access.SetEnabledAsync("", true, token);
        var first = rig.Session;
        Assert.True(await RequiresConfirmation(first, storage));
        var saved = await rig.Access.SetInstanceApprovalAsync("", approval, true, token);
        Assert.Equal(approval, Assert.Single(saved.InstanceApprovals!));
        Assert.True(first.Revoked.IsCancellationRequested);
        var next = rig.Session;
        Assert.False(await RequiresConfirmation(next, storage));
        Assert.True(await RequiresConfirmation(next, other));
        Assert.False((await Stat(rig.Session, storage, "words.json")).StructuredContent!.Value.GetProperty("exists").GetBoolean());
        Assert.True((await Stat(next, other, "words.json")).IsError);

        await rig.Access.SetExclusionsAsync("", [new("", storage.Id, storage.TypeId, "exports", "private/")], token);
        Assert.True((await Stat(rig.Session, storage, "private/words.json")).IsError);
        Assert.Equal(1, rig.Store.Opened);
        Assert.False(await RequiresConfirmation(rig.Session, storage));
        var changed = storage with { Revision = "v2" };
        rig.Store.Targets = [changed, other];
        Assert.True(await RequiresConfirmation(rig.Session, changed));
        Assert.True((await Stat(rig.Session, changed, "words.json")).IsError);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Access.SetInstanceApprovalAsync("", approval, true, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Access.SetInstanceApprovalAsync("other-owner", approval, true, token));

        var updated = approval with { Revision = changed.Revision };
        await rig.Access.SetInstanceApprovalAsync("", updated, true, token);
        var beforeRemoval = rig.Session;
        await rig.Access.SetInstanceApprovalAsync("", updated, false, token);
        Assert.True(beforeRemoval.Revoked.IsCancellationRequested);
        Assert.True(await RequiresConfirmation(rig.Session, changed));
        await rig.Access.SetInstanceApprovalAsync("", updated, true, token);
        var blocked = await rig.Access.SetExclusionsAsync("", [new("", changed.Id, changed.TypeId)], token);
        Assert.Empty(blocked.InstanceApprovals!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Access.SetInstanceApprovalAsync("", updated, true, token));
        await rig.Access.SetExclusionsAsync("", [], token);
        Assert.True(await RequiresConfirmation(rig.Session, changed));

        async Task<bool> RequiresConfirmation(McpSessionAccess session, McpTarget target)
        {
            var list = await rig.Tools.InvokeAsync(session, "hermod_list_instances", null, rig.Cursors, token);
            JsonElement result = list.StructuredContent!.Value;
            Assert.Equal(Text.Get("McpInstanceConfirmationHint"), result.GetProperty("confirmation_hint").GetString());
            return result.GetProperty("instances").EnumerateArray().Single(row => row.GetProperty("instance_id").GetString() == target.Id)
                .GetProperty("scope").GetProperty("requires_confirmation").GetBoolean();
        }
        Task<CallToolResult> Stat(McpSessionAccess session, McpTarget target, string key) =>
            rig.Tools.InvokeAsync(session, McpToolCatalog.StorageStat, McpTestRig.Inputs(McpDefaultAccess.InstanceGrant(session, target),
                ("bucket", "exports"), ("key", key)), rig.Cursors, token);
    }
}
