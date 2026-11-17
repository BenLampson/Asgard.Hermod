using Asgard.Hermod.ObjectStorage.Abstractions.Clients;
using Asgard.Hermod.ObjectStorage.Abstractions.Models;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpStorageToolTests
{
    [Fact]
    public async Task StorageScopesListingAndUsesOpaqueBoundContinuation()
    {
        int listings = 0;
        var client = ProtocolStub<IObjectStorageClient>.For((name, arguments) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.ListObjectsAsync), name);
            Assert.Equal("exports", arguments[0]);
            Assert.Equal("nightly/", arguments[1]);
            if (++listings == 1)
            {
                Assert.Null(arguments[2]);
                return Task.FromResult(new ObjectStoragePage([new("nightly/result.csv", false, 10), new("private/key", false)], "provider-pointer"));
            }
            Assert.Equal("provider-pointer", arguments[2]);
            return Task.FromResult(new ObjectStoragePage([new("nightly/next.csv", false)]));
        });
        var target = new McpTarget("object-store", "objects", "storage", "v1");
        using var rig = new McpTestRig([target], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client));
        var grant = await rig.GrantAsync(target, "exports", "nightly/");
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        Assert.True((await rig.CallAsync("hermod_storage_list", grant, ("prefix", ""))).IsError);
        Assert.True((await rig.CallAsync("hermod_storage_stat", grant, ("key", "nightly/../private/key"))).IsError);
        Assert.True((await rig.CallAsync("hermod_storage_stat", grant, ("key", "nightly/%2E%2E/private/key"))).IsError);
        Assert.True((await rig.CallAsync("hermod_storage_read_text", grant, ("key", "nightly/result.csv"))).IsError);
        Assert.Equal(0, listings);

        var first = (await rig.CallAsync("hermod_storage_list", grant)).StructuredContent!.Value;
        Assert.Contains("nightly/result.csv", first.GetRawText());
        Assert.DoesNotContain("private/key", first.GetRawText());
        Assert.DoesNotContain("provider-pointer", first.GetRawText());
        string cursor = first.GetProperty("next_cursor").GetString()!;
        Assert.True((await rig.CallAsync("hermod_storage_list", grant, ("prefix", "nightly/other/"), ("cursor", cursor))).IsError);
        var next = (await rig.CallAsync("hermod_storage_list", grant, ("cursor", cursor))).StructuredContent!.Value;
        Assert.Contains("nightly/next.csv", next.GetRawText());
        Assert.Equal(2, listings);
    }

    [Fact]
    public async Task GrantedTextIsBoundedAndProviderErrorIsRedacted()
    {
        var client = ProtocolStub<IObjectStorageClient>.For((name, arguments) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.OpenReadAsync), name);
            Assert.Equal("exports", arguments[0]);
            Assert.Equal(16 * 1024 + 1, arguments[2]);
            if (Equals(arguments[1], "logs/failure.txt")) throw new IOException("connection string or credential must be hidden");
            byte[] data = new byte[17 * 1024];
            Array.Fill(data, (byte)'a');
            return Task.FromResult(new ObjectStorageReadResult(new MemoryStream(data), data.Length));
        });
        var target = new McpTarget("object-store", "objects", "storage", "v1");
        using var rig = new McpTestRig([target], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client));
        var grant = await rig.GrantAsync(target, "exports", "logs/", share: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var result = (await rig.CallAsync("hermod_storage_read_text", grant, ("key", "logs/result.txt"))).StructuredContent!.Value;
        Assert.True(result.GetProperty("truncated").GetBoolean());
        Assert.Equal(16 * 1024, result.GetProperty("content").GetString()!.Length);
        var failure = await rig.CallAsync("hermod_storage_read_text", grant, ("key", "logs/failure.txt"));
        Assert.True(failure.IsError);
        Assert.DoesNotContain("credential must be hidden", Assert.Single(failure.Content).ToString());
    }
}
