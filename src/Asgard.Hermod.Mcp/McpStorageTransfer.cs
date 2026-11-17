using Asgard.Hermod.ObjectStorage;
using Asgard.Hermod.ObjectStorage.Abstractions.Clients;
using Asgard.Hermod.ObjectStorage.Abstractions.Services;

namespace Asgard.Hermod.Mcp;

/// <summary>在固定授权内规划文件或目录传输；每个实际文件开始前重新检查授权和本地路径。</summary>
internal sealed class McpStorageTransfer(McpAccessService access, McpStorageClients clients)
{
    private const int FileLimit = 1000;
    private const int DirectoryLimit = 4000;
    private const int PageLimit = 200;

    internal async Task RunAsync(McpSessionAccess session, McpGrant grant, bool upload, bool overwrite, string key, string local,
        Action<int, int> report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var connection = access.OpenTarget(session, grant);
        McpStorageTransferPaths.NormalizeRoot(grant.TransferRoot!);
        using var client = await clients.CreateAsync(grant, connection, token).ConfigureAwait(false);
        var plan = upload ? PlanUpload(key, local, token) : await PlanDownloadAsync(client, session, grant, key, local, token).ConfigureAwait(false);
        report(plan.Count, 0);
        if (plan.Count == 0) throw Text.Exception("McpTransferNoFiles");
        if (!upload)
            foreach (var file in plan) EnsureDestinationAvailable(file.Path, overwrite);

        int completed = 0;
        foreach (var file in plan)
        {
            token.ThrowIfCancellationRequested();
            access.Check(session, grant);
            if (upload)
            {
                EnsureSource(file.Path, directory: false);
                try
                {
                    await ObjectStorageFileTransfer.UploadAsync(client, grant.Resource, file.Key, file.Path, overwrite,
                        progress: null, token).ConfigureAwait(false);
                }
                catch (ObjectStorageException error) when (error.ErrorCode is "ObjectAlreadyExists" or "FileAlreadyExists")
                {
                    throw Text.Exception("McpTransferDestinationExists");
                }
            }
            else
            {
                EnsureDestinationAvailable(file.Path, overwrite);
                try
                {
                    await ObjectStorageFileTransfer.DownloadAsync(client, grant.Resource, file.Key, file.Path, overwrite,
                        progress: null, token).ConfigureAwait(false);
                }
                catch (IOException) when (!overwrite && (File.Exists(file.Path) || Directory.Exists(file.Path)))
                {
                    throw Text.Exception("McpTransferDestinationExists");
                }
            }
            report(plan.Count, ++completed);
        }
    }

    private static List<(string Key, string Path)> PlanUpload(string key, string path, CancellationToken token)
    {
        bool isDirectory = key.EndsWith('/');
        EnsureSource(path, isDirectory);
        if (!isDirectory) return [(key, path)];

        var files = new List<(string Key, string Path)>();
        var pending = new Stack<string>();
        pending.Push(path);
        int directories = 1;
        while (pending.TryPop(out string? directory))
        {
            token.ThrowIfCancellationRequested();
            EnsureSource(directory, directory: true);
            foreach (string child in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attributes = McpStorageTransferPaths.CheckExisting(child, allowMissing: false)!.Value;
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (++directories > DirectoryLimit) throw Text.Exception("McpTransferTooManyFiles");
                    pending.Push(child);
                    continue;
                }
                string target = key + Path.GetRelativePath(path, child).Replace('\\', '/');
                if (!McpStorageTransferPaths.IsKey(target)) throw Text.Exception("McpTransferInvalidKey");
                if (files.Count >= FileLimit) throw Text.Exception("McpTransferTooManyFiles");
                files.Add((target, child));
            }
        }
        return files;
    }

    private async Task<List<(string Key, string Path)>> PlanDownloadAsync(IObjectStorageClient client, McpSessionAccess session,
        McpGrant grant, string key, string path, CancellationToken token)
    {
        if (!key.EndsWith('/')) return [(key, path)];

        var files = new List<(string Key, string Path)>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        int pages = 0;
        do
        {
            token.ThrowIfCancellationRequested();
            access.Check(session, grant);
            if (++pages > PageLimit) throw Text.Exception("McpTransferTooManyFiles");
            var page = await client.ListObjectsAsync(grant.Resource, key, continuation, null, 250, token).ConfigureAwait(false);
            foreach (var entry in page.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (!entry.Key.StartsWith(key, StringComparison.Ordinal) || !McpStorageTransferPaths.IsKey(entry.Key))
                    throw Text.Exception("McpTransferInvalidKey");
                if (entry.IsDirectory || entry.Key.EndsWith('/')) continue;
                string target = McpStorageTransferPaths.Resolve(path, entry.Key[key.Length..], directory: false);
                if (!paths.Add(target)) throw Text.Exception("McpTransferDestinationExists");
                if (files.Count >= FileLimit) throw Text.Exception("McpTransferTooManyFiles");
                files.Add((entry.Key, target));
            }
            continuation = page.NextToken;
            if (continuation is not null && (continuation.Length is 0 or > 16384 || !tokens.Add(continuation)))
                throw Text.Exception("McpTransferInvalidListing");
        } while (continuation is not null);

        // OSS 可同时保存 a 与 a/b，在 Windows 上不能同时创建同名文件和目录。
        foreach (var file in files)
            for (string? parent = Path.GetDirectoryName(file.Path); parent is not null && !string.Equals(parent, path, StringComparison.OrdinalIgnoreCase);
                parent = Path.GetDirectoryName(parent))
                if (paths.Contains(parent)) throw Text.Exception("McpTransferDestinationExists");
        return files;
    }

    private static void EnsureSource(string path, bool directory)
    {
        if (McpStorageTransferPaths.CheckExisting(path, allowMissing: false) is not { } attributes
            || attributes.HasFlag(FileAttributes.Directory) != directory)
            throw Text.Exception("McpTransferSourceType");
    }

    private static void EnsureDestinationAvailable(string path, bool overwrite)
    {
        if (McpStorageTransferPaths.CheckExisting(path, allowMissing: true) is { } existing
            && (!overwrite || existing.HasFlag(FileAttributes.Directory)))
            throw Text.Exception("McpTransferDestinationExists");
    }
}
