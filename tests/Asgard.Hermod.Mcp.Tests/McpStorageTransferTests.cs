using System.Text.Json;
using Asgard.Hermod.ObjectStorage.Abstractions.Clients;
using Asgard.Hermod.ObjectStorage.Abstractions.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpStorageTransferTests
{
    private static readonly McpTarget Target = new("object-store", "OSS", "storage", "v1");
    private const string Start = McpToolCatalog.StorageStartTransfer;
    private const string Status = McpToolCatalog.StorageTransferStatus;

    [Fact]
    public async Task SdkClientUsesTheSameBucketRegionForListingAndBackgroundDownload()
    {
        using var folder = new TransferFolder();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] contents = [1, 2, 3];
        List<string?> regions = [];
        var lookup = ProtocolStub<IObjectStorageBucketRegionClient>.For((name, args) =>
        {
            Assert.Equal(nameof(IObjectStorageBucketRegionClient.FindBucketRegionAsync), name);
            Assert.Equal("exports", args[0]);
            return Task.FromResult<string?>("cn-chengdu");
        });
        var routed = ProtocolStub<IObjectStorageClient>.For((name, args) =>
        {
            Assert.Equal("exports", args[0]);
            if (name == nameof(IObjectStorageClient.ListObjectsAsync))
            {
                Assert.Equal("test/reports/", args[1]);
                Assert.Null(args[3]);
                if (Equals(args[4], 30))
                    return Task.FromResult(new ObjectStoragePage([new("test/reports/2026/result.bin", false, contents.Length)]));
                return args[2] is null
                    ? Task.FromResult(new ObjectStoragePage([new("test/reports/", true)], "page-two"))
                    : Task.FromResult(new ObjectStoragePage([new("test/reports/2026/result.bin", false, contents.Length)]));
            }
            Assert.Equal(nameof(IObjectStorageClient.OpenReadAsync), name);
            Assert.Equal("test/reports/2026/result.bin", args[1]);
            return ReadAsync((CancellationToken)args[3]!);
        });
        using var rig = new McpTestRig([Target], storage: ProtocolStub<IObjectStorageClientFactory>.For((name, args) =>
        {
            Assert.Equal(nameof(IObjectStorageClientFactory.CreateClient), name);
            string? region = (string?)args[1];
            regions.Add(region);
            if (region is null) return lookup;
            Assert.Equal("cn-chengdu", region);
            return routed;
        }));
        var grant = await rig.GrantAsync(Target, "exports", "test/", transferRoot: folder.Path, download: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        string pipeName = "Hermod.Mcp.Tests." + Guid.NewGuid().ToString("N");
        await using var server = new McpDesktopServer(rig.Access, new(rig.Tools), name: pipeName);
        server.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(McpTestRig.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        await using var pipe = await McpPipeEndpoint.ConnectAsync(pipeName, token);
        Assert.True(await McpPipeEndpoint.AuthenticateAsync(pipe, rig.Session.Policy.Secret, token));
        await using var client = await McpClient.CreateAsync(new StreamClientTransport(pipe, pipe), cancellationToken: token);
        var tools = await client.ListToolsAsync(cancellationToken: token);
        Assert.False(Assert.Single(tools, tool => tool.Name == Start).ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.True(Assert.Single(tools, tool => tool.Name == Status).ProtocolTool.Annotations!.ReadOnlyHint);
        var listed = GetResult(await client.CallToolAsync("hermod_storage_list", new Dictionary<string, object?>
        {
            ["grant_id"] = grant.Id,
            ["prefix"] = "test/reports/",
        }, cancellationToken: token));
        Assert.Equal("test/reports/2026/result.bin", Assert.Single(listed.GetProperty("entries").EnumerateArray()).GetProperty("key").GetString());

        var queued = GetResult(await client.CallToolAsync(Start, new Dictionary<string, object?>
        {
            ["grant_id"] = grant.Id,
            ["direction"] = "download",
            ["key"] = "test/reports/",
            ["local_path"] = "download",
        }, cancellationToken: token));
        Assert.Equal("queued", queued.GetProperty("status").GetString());
        string id = queued.GetProperty("task_id").GetString()!;
        await opened.Task.WaitAsync(token);
        var statusArgs = new Dictionary<string, object?> { ["task_id"] = id };
        var running = GetResult(await client.CallToolAsync(Status, statusArgs, cancellationToken: token));
        Assert.Equal("running", running.GetProperty("status").GetString());
        Assert.Equal(0, running.GetProperty("completed_files").GetInt32());
        Assert.Equal(1, running.GetProperty("total_files").GetInt32());
        release.SetResult();

        var terminal = await WaitForTerminalAsync(() => client.CallToolAsync(Status, statusArgs, cancellationToken: token).AsTask(), token);
        Assert.Equal("completed", terminal.GetProperty("status").GetString());
        Assert.Equal(1, terminal.GetProperty("completed_files").GetInt32());
        Assert.Equal(contents, await File.ReadAllBytesAsync(Path.Combine(folder.Path, "download", "2026", "result.bin"), token));
        Assert.True((await client.CallToolAsync(Status, statusArgs, cancellationToken: token)).IsError);
        Assert.Equal([null, "cn-chengdu", "cn-chengdu"], regions);

        async Task<ObjectStorageReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            opened.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new(new MemoryStream(contents), contents.Length);
        }
    }

    [Fact]
    public async Task UploadDirectoryKeepsHierarchyAndUsesServerSideNoOverwrite()
    {
        using var folder = new TransferFolder();
        string sub = Directory.CreateDirectory(Path.Combine(folder.Path, "out", "daily")).FullName;
        await File.WriteAllTextAsync(Path.Combine(sub, "report.csv"), "sample", McpTestRig.Token);
        using var rig = CreateRig(ProtocolStub<IObjectStorageClient>.For((name, args) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.UploadAsync), name);
            Assert.Equal("exports", args[0]);
            Assert.Equal("test/archive/daily/report.csv", args[1]);
            Assert.Equal(6L, args[3]);
            Assert.Equal(false, args[4]);
            using var reader = new StreamReader((Stream)args[2]!, leaveOpen: true);
            Assert.Equal("sample", reader.ReadToEnd());
            return Task.CompletedTask;
        }));
        var grant = await rig.GrantAsync(Target, "exports", "test/", transferRoot: folder.Path, upload: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        string id = TaskId(await rig.CallAsync(Start, grant, ("direction", "upload"), ("key", "test/archive/"), ("local_path", "out")));
        var terminal = await WaitForTerminalAsync(() => PollAsync(rig, id), McpTestRig.Token);
        Assert.Equal("completed", terminal.GetProperty("status").GetString());
        Assert.Equal(1, terminal.GetProperty("completed_files").GetInt32());
        Assert.True((await PollAsync(rig, id)).IsError);
    }

    [Fact]
    public async Task NewUploadAllowsAbsoluteSourcesOutsideDownloadFolderAndBucketRoot()
    {
        using var downloadFolder = new TransferFolder();
        using var sourceFolder = new TransferFolder();
        string sourceFile = Path.Combine(sourceFolder.Path, "report.txt");
        await File.WriteAllTextAsync(sourceFile, "outside download folder", McpTestRig.Token);
        var uploaded = new List<string>();
        using var rig = CreateRig(ProtocolStub<IObjectStorageClient>.For((name, args) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.UploadAsync), name);
            uploaded.Add((string)args[1]!);
            return Task.CompletedTask;
        }));
        var grant = await rig.GrantAsync(Target, "exports", "", transferRoot: downloadFolder.Path,
            upload: true, download: true, unrestrictedUpload: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var discovery = GetResult(await rig.Tools.InvokeAsync(rig.Session, "hermod_list_instances", null, rig.Cursors, McpTestRig.Token));
        Assert.True(Assert.Single(discovery.GetProperty("instances").EnumerateArray())
            .GetProperty("scope").GetProperty("allow_unrestricted_upload").GetBoolean());
        Assert.True((await rig.CallAsync(Start, grant, ("direction", "upload"), ("key", "folder/report.txt"),
            ("local_path", "report.txt"))).IsError);
        string fileTask = TaskId(await rig.CallAsync(Start, grant, ("direction", "upload"), ("key", "folder/report.txt"),
            ("local_path", sourceFile)));
        Assert.Equal("completed", (await WaitForTerminalAsync(() => PollAsync(rig, fileTask), McpTestRig.Token)).GetProperty("status").GetString());
        string directoryTask = TaskId(await rig.CallAsync(Start, grant, ("direction", "upload"), ("key", "folder/tree/"),
            ("local_path", sourceFolder.Path)));
        Assert.Equal("completed", (await WaitForTerminalAsync(() => PollAsync(rig, directoryTask), McpTestRig.Token)).GetProperty("status").GetString());
        Assert.Equal(["folder/report.txt", "folder/tree/report.txt"], uploaded);
        Assert.Empty(Directory.EnumerateFileSystemEntries(downloadFolder.Path));
    }

    [Fact]
    public async Task UploadOnlyNeedsNoDownloadFolderAndLegacyGrantsStayRestricted()
    {
        using var permitted = new TransferFolder();
        using var outside = new TransferFolder();
        string externalFile = Path.Combine(outside.Path, "source.txt");
        await File.WriteAllTextAsync(externalFile, "sample", McpTestRig.Token);
        using var rig = CreateRig(ProtocolStub<IObjectStorageClient>.For((_, _) => Task.CompletedTask));
        var unrestricted = await rig.GrantAsync(Target, "exports", "", upload: true, unrestrictedUpload: true);
        Assert.Null(unrestricted.TransferRoot);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        string id = TaskId(await rig.CallAsync(Start, unrestricted, ("direction", "upload"), ("key", "source.txt"), ("local_path", externalFile)));
        Assert.Equal("completed", (await WaitForTerminalAsync(() => PollAsync(rig, id), McpTestRig.Token)).GetProperty("status").GetString());

        var legacy = await rig.GrantAsync(Target, "exports", "legacy/", transferRoot: permitted.Path, upload: true);
        Assert.False(legacy.AllowUnrestrictedUpload);
        Assert.True((await rig.CallAsync(Start, legacy, ("direction", "upload"), ("key", "legacy/source.txt"), ("local_path", externalFile))).IsError);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.GrantAsync(Target, "exports", "", upload: true));
        var oldJson = JsonSerializer.Serialize(new
        {
            legacy.Id,
            legacy.InstanceId,
            legacy.TypeId,
            legacy.Revision,
            legacy.Resource,
            legacy.Qualifier,
            legacy.Table,
            legacy.ShareContent,
            legacy.TransferRoot,
            legacy.AllowUpload,
            legacy.AllowDownload
        });
        Assert.False(JsonSerializer.Deserialize<McpGrant>(oldJson)!.AllowUnrestrictedUpload);
    }

    [Fact]
    public void DownloadDestinationMayBeTheDriveRoot()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal(root, McpStorageTransferPaths.NormalizeRoot(root));
        Assert.Equal(Path.Combine(root, "example.txt"), McpStorageTransferPaths.Resolve(root, "example.txt", directory: false));
    }

    [Fact]
    public async Task UnrestrictedDirectoryUploadFollowsLinksButRejectsCycles()
    {
        using var source = new TransferFolder();
        using var destination = new TransferFolder();
        await File.WriteAllTextAsync(Path.Combine(destination.Path, "linked.txt"), "linked", McpTestRig.Token);
        string link = Path.Combine(source.Path, "linked");
        string cycle = Path.Combine(destination.Path, "cycle");
        CreateDirectoryLink(link, destination.Path);
        try
        {
            using var rig = CreateRig(ProtocolStub<IObjectStorageClient>.For((name, args) =>
            {
                Assert.Equal(nameof(IObjectStorageClient.UploadAsync), name);
                Assert.Equal("uploads/linked/linked.txt", args[1]);
                return Task.CompletedTask;
            }));
            var grant = await rig.GrantAsync(Target, "exports", "", upload: true, unrestrictedUpload: true);
            await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
            string uploaded = TaskId(await rig.CallAsync(Start, grant, ("direction", "upload"), ("key", "uploads/"), ("local_path", source.Path)));
            Assert.Equal("completed", (await WaitForTerminalAsync(() => PollAsync(rig, uploaded), McpTestRig.Token)).GetProperty("status").GetString());

            CreateDirectoryLink(cycle, source.Path);
            string cyclic = TaskId(await rig.CallAsync(Start, grant, ("direction", "upload"), ("key", "uploads/"), ("local_path", source.Path)));
            var terminal = await WaitForTerminalAsync(() => PollAsync(rig, cyclic), McpTestRig.Token);
            Assert.Equal("failed", terminal.GetProperty("status").GetString());
            Assert.Equal(Text.Get("McpTransferUploadCycle"), terminal.GetProperty("error").GetString());
        }
        finally
        {
            // 显式删除本次用例创建的联接本身，避免递归清理沿循环访问目标。
            if (Directory.Exists(cycle)) Directory.Delete(cycle);
            if (Directory.Exists(link)) Directory.Delete(link);
        }
    }

    [Fact]
    public async Task TransferIsOptInAndCannotEscapeEitherScope()
    {
        using var folder = new TransferFolder();
        using var rig = new McpTestRig([Target]);
        var metadata = await rig.GrantAsync(Target, "exports", "test/");
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        Assert.DoesNotContain(rig.Tools.List(rig.Session), tool => tool.Name is Start or Status);
        Assert.True((await rig.CallAsync(Start, metadata, ("direction", "download"), ("key", "test/report"))).IsError);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.GrantAsync(Target, "exports", "invalid-prefix", transferRoot: folder.Path, download: true));

        var grant = await rig.GrantAsync(Target, "exports", "test/", transferRoot: folder.Path, download: true);
        Assert.Contains(rig.Tools.List(rig.Session), tool => tool.Name == Start);
        Assert.True((await rig.CallAsync(Start, grant, ("direction", "upload"), ("key", "test/report"), ("local_path", "report"))).IsError);
        foreach (string key in new[] { "testing/report", "test/../private", "test/%2e%2e/private", "test//report" })
            Assert.True((await rig.CallAsync(Start, grant, ("direction", "download"), ("key", key))).IsError);
        foreach (string path in new[] { "../outside", @"C:\outside", @"\\server\share\outside", "report:stream", "NUL.txt", "COM¹.txt" })
            Assert.True((await rig.CallAsync(Start, grant, ("direction", "download"), ("key", "test/report"), ("local_path", path))).IsError);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder.Path));
    }

    [Fact]
    public async Task AFileCreatedDuringDownloadIsNeverOverwrittenAndFailureIsConsumed()
    {
        using var folder = new TransferFolder();
        string destination = Path.Combine(folder.Path, "report.csv");
        using var rig = CreateRig(ProtocolStub<IObjectStorageClient>.For((name, _) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.OpenReadAsync), name);
            File.WriteAllText(destination, "keep");
            return Task.FromResult(new ObjectStorageReadResult(new MemoryStream([9]), 1));
        }));
        var grant = await rig.GrantAsync(Target, "exports", "test/", transferRoot: folder.Path, download: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        string id = TaskId(await rig.CallAsync(Start, grant, ("direction", "download"), ("key", "test/report.csv")));
        var terminal = await WaitForTerminalAsync(() => PollAsync(rig, id), McpTestRig.Token);
        Assert.Equal("failed", terminal.GetProperty("status").GetString());
        Assert.Equal(Text.Get("McpTransferDestinationExists"), terminal.GetProperty("error").GetString());
        Assert.Equal("keep", await File.ReadAllTextAsync(destination, McpTestRig.Token));
        Assert.Equal(destination, Assert.Single(Directory.GetFiles(folder.Path)));
        Assert.True((await PollAsync(rig, id)).IsError);
    }

    [Fact]
    public async Task OverwriteRequiresExplicitGrantAndRequestForBothDirections()
    {
        using var folder = new TransferFolder();
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "source.txt"), "new upload", McpTestRig.Token);
        string destination = Path.Combine(folder.Path, "download.txt");
        await File.WriteAllTextAsync(destination, "old download", McpTestRig.Token);
        int uploads = 0, downloads = 0;
        using var rig = CreateRig(ProtocolStub<IObjectStorageClient>.For((name, args) =>
        {
            if (name == nameof(IObjectStorageClient.UploadAsync))
            {
                uploads++;
                Assert.Equal(true, args[4]);
                return Task.CompletedTask;
            }
            Assert.Equal(nameof(IObjectStorageClient.OpenReadAsync), name);
            downloads++;
            byte[] data = System.Text.Encoding.UTF8.GetBytes("new download");
            return Task.FromResult(new ObjectStorageReadResult(new MemoryStream(data), data.Length));
        }));
        var ordinary = await rig.GrantAsync(Target, "exports", "test/", transferRoot: folder.Path, upload: true, download: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        Assert.True((await rig.CallAsync(Start, ordinary, ("direction", "upload"), ("key", "test/source.txt"),
            ("local_path", "source.txt"), ("conflict", "overwrite"))).IsError);
        var grant = await rig.GrantAsync(Target, "exports", "test/", transferRoot: folder.Path, upload: true, download: true, overwrite: true);

        string preserved = TaskId(await rig.CallAsync(Start, grant, ("direction", "download"), ("key", "test/download.txt")));
        Assert.Equal("failed", (await WaitForTerminalAsync(() => PollAsync(rig, preserved), McpTestRig.Token)).GetProperty("status").GetString());
        Assert.Equal("old download", await File.ReadAllTextAsync(destination, McpTestRig.Token));
        string upload = TaskId(await rig.CallAsync(Start, grant, ("direction", "upload"), ("key", "test/source.txt"),
            ("local_path", "source.txt"), ("conflict", "overwrite")));
        Assert.Equal("completed", (await WaitForTerminalAsync(() => PollAsync(rig, upload), McpTestRig.Token)).GetProperty("status").GetString());
        string download = TaskId(await rig.CallAsync(Start, grant, ("direction", "download"), ("key", "test/download.txt"),
            ("conflict", "overwrite")));
        Assert.Equal("completed", (await WaitForTerminalAsync(() => PollAsync(rig, download), McpTestRig.Token)).GetProperty("status").GetString());
        Assert.Equal("new download", await File.ReadAllTextAsync(destination, McpTestRig.Token));
        Assert.Equal(1, uploads);
        Assert.Equal(1, downloads);
    }

    [Fact]
    public async Task AnUnclaimedFinalResultExpires()
    {
        using var folder = new TransferFolder();
        var clock = new CompletionClock();
        var client = ProtocolStub<IObjectStorageClient>.For((name, _) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.OpenReadAsync), name);
            clock.WatchCompletion();
            return Task.FromResult(new ObjectStorageReadResult(new MemoryStream([42]), 1));
        });
        using var rig = new McpTestRig([Target], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client), clock: clock);
        var grant = await rig.GrantAsync(Target, "exports", "test/", transferRoot: folder.Path, download: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        string id = TaskId(await rig.CallAsync(Start, grant, ("direction", "download"), ("key", "test/report.csv")));
        await clock.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), McpTestRig.Token);
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.True((await PollAsync(rig, id)).IsError);
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(Path.Combine(folder.Path, "report.csv"), McpTestRig.Token));
    }

    [Fact]
    public async Task RevocationStopsInFlightTransferAndTheNextPairingCannotReadItsId()
    {
        using var folder = new TransferFolder();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var rig = CreateRig(ProtocolStub<IObjectStorageClient>.For((name, args) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.OpenReadAsync), name);
            return WaitUntilCanceledAsync((CancellationToken)args[3]!);
        }));
        var grant = await rig.GrantAsync(Target, "exports", "test/", transferRoot: folder.Path, download: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        string id = TaskId(await rig.CallAsync(Start, grant, ("direction", "download"), ("key", "test/report.csv")));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), McpTestRig.Token);
        await rig.Access.SetEnabledAsync("", false, McpTestRig.Token);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5), McpTestRig.Token);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        Assert.True((await PollAsync(rig, id)).IsError);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder.Path));

        async Task<ObjectStorageReadResult> WaitUntilCanceledAsync(CancellationToken token)
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { canceled.TrySetResult(); }
            throw new InvalidOperationException();
        }
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); return; }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { }
        // Windows 通常无需管理员权限即可创建目录联接，作为符号链接不可用时的等价测试。
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start);
        if (process is null) { Assert.Skip("The test environment cannot create a directory link."); return; }
        process.WaitForExit();
        if (process.ExitCode != 0) Assert.Skip("The test environment cannot create a directory link.");
    }

    private static McpTestRig CreateRig(IObjectStorageClient client) =>
        new([Target], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client));

    private static JsonElement GetResult(CallToolResult result)
    {
        Assert.False(result.IsError == true, string.Join(" ", result.Content.OfType<TextContentBlock>().Select(item => item.Text)));
        return result.StructuredContent!.Value;
    }

    private static string TaskId(CallToolResult result) => GetResult(result).GetProperty("task_id").GetString()!;

    private static Task<CallToolResult> PollAsync(McpTestRig rig, string id) =>
        rig.Tools.InvokeAsync(rig.Session, Status, new Dictionary<string, JsonElement> { ["task_id"] = JsonSerializer.SerializeToElement(id) },
            rig.Cursors, McpTestRig.Token);

    private static async Task<JsonElement> WaitForTerminalAsync(Func<Task<CallToolResult>> poll, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            var result = GetResult(await poll());
            if (result.GetProperty("status").GetString() is "completed" or "failed") return result;
            await Task.Delay(15, deadline.Token);
        }
    }

    private sealed class TransferFolder : IDisposable
    {
        private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("Hermod.Mcp.Transfer.");
        internal string Path => _folder.FullName;
        public void Dispose() => _folder.Delete(recursive: true);
    }

    private sealed class CompletionClock : TimeProvider
    {
        private long _timestamp;
        private int _watch;
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        internal void WatchCompletion() => Volatile.Write(ref _watch, 1);
        internal void Advance(TimeSpan time) => Interlocked.Add(ref _timestamp, time.Ticks);

        public override long GetTimestamp()
        {
            long timestamp = Interlocked.Read(ref _timestamp);
            if (Volatile.Read(ref _watch) == 1) Finished.TrySetResult();
            return timestamp;
        }
    }
}
