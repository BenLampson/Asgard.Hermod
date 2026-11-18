using Asgard.Hermod.ObjectStorage.Abstractions.Clients;
using Asgard.Hermod.ObjectStorage.Abstractions.Models;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpStorageScopeBrowserTests
{
    [Fact]
    public async Task BrowsesOnlyImmediateFoldersAndRetainsBucketRegionAndContinuation()
    {
        var target = new McpTarget("oss", "Objects", "storage", "v1");
        var store = new MemoryMcpStore(target);
        int reads = 0;
        var client = ProtocolStub<IObjectStorageClient>.For((name, args) => name switch
        {
            nameof(IObjectStorageClient.ListBucketsAsync) => Task.FromResult<IReadOnlyList<ObjectStorageBucket>>([new("exports", "region-1")]),
            nameof(IObjectStorageClient.ListObjectsAsync) => Page(args),
            _ => throw new InvalidOperationException(name),
        });
        var factory = ProtocolStub<IObjectStorageClientFactory>.For((_, args) =>
        {
            if (reads > 0) Assert.Equal("region-1", args[1]);
            return client;
        });
        var browser = new McpStorageScopeBrowser(store, factory);
        var bucket = Assert.Single(await browser.ListBucketsAsync("", target, McpTestRig.Token));
        reads++;
        var first = await browser.ListFoldersAsync("", target, bucket, "logs/", null, McpTestRig.Token);
        Assert.Equal(["logs/one/"], first.Folders);
        Assert.Equal("next", first.NextToken);
        var second = await browser.ListFoldersAsync("", target, bucket, "logs/", first.NextToken, McpTestRig.Token);
        Assert.Equal(["logs/two/"], second.Folders);
        Assert.Null(second.NextToken);
        Assert.Null(store.Policy);

        static Task<ObjectStoragePage> Page(object?[] args)
        {
            Assert.Equal("exports", args[0]); Assert.Equal("logs/", args[1]); Assert.Equal("/", args[3]);
            return Task.FromResult(args[2] is null
                ? new ObjectStoragePage([new("logs/one/", true), new("logs/data.csv", false), new("logs/one/deeper/", true)], "next")
                : new ObjectStoragePage([new("logs/two/", true)]));
        }
    }

    [Fact]
    public async Task DiscardsResultsIfConnectionChangesDuringListing()
    {
        var target = new McpTarget("oss", "Objects", "storage", "v1");
        var store = new MemoryMcpStore(target);
        var pending = new TaskCompletionSource<IReadOnlyList<ObjectStorageBucket>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = ProtocolStub<IObjectStorageClient>.For((_, _) => pending.Task);
        var factory = ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client);
        var browser = new McpStorageScopeBrowser(store, factory);
        var request = browser.ListBucketsAsync("", target, McpTestRig.Token);
        store.Targets = [target with { Revision = "v2" }];
        pending.SetResult([new("exports", "region")]);
        await Assert.ThrowsAnyAsync<Exception>(() => request);
        Assert.Null(store.Policy);
    }

    [Fact]
    public async Task RejectsOutOfRangeFoldersAndRepeatedContinuation()
    {
        var target = new McpTarget("oss", "Objects", "storage", "v1");
        var store = new MemoryMcpStore(target);
        var client = ProtocolStub<IObjectStorageClient>.For((_, _) => Task.FromResult(new ObjectStoragePage([new("other/", true)], "next")));
        var factory = ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client);
        var browser = new McpStorageScopeBrowser(store, factory);
        await Assert.ThrowsAnyAsync<Exception>(() => browser.ListFoldersAsync("", target, new("exports", ""), "logs/", "next", McpTestRig.Token));
    }
}
