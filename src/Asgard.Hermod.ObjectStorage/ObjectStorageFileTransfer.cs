namespace Asgard.Hermod.ObjectStorage;

/// <summary>使用已经选定的资源客户端传输本地文件，不重新解析实例或工作区。</summary>
public static class ObjectStorageFileTransfer
{
    /// <summary>流式读取本地文件，由提供方执行服务端的覆盖规则。</summary>
    public static async Task UploadAsync(IObjectStorageClient client, string bucket, string key, string path, bool overwrite,
        IProgress<ObjectStorageTransferProgress>? progress, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await client.UploadAsync(bucket, key, file, file.Length, overwrite, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>下载到同目录的临时文件，校验完整性后原子落盘；未授权覆盖时保留既有文件。</summary>
    public static async Task DownloadAsync(IObjectStorageClient client, string bucket, string key, string path, bool overwrite,
        IProgress<ObjectStorageTransferProgress>? progress, CancellationToken cancellationToken, long? maxBytes = null)
    {
        if (maxBytes is <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        string destination = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(destination)!;
        EnsureDownloadDirectory(directory, createMissing: true);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.download");
        bool created = false;
        try
        {
            long transferred = 0;
            long totalBytes;
            await using (ObjectStorageReadResult remote = await client.OpenReadAsync(bucket, key, null,
                cancellationToken).ConfigureAwait(false))
            {
                totalBytes = remote.Size;
                if (maxBytes is { } limit && remote.Size > limit)
                    throw ObjectStorageException.FromResource("FilePreviewTooLarge", "PreviewLimitExceeded");
                // 连接准备可能耗时；在创建临时文件之前重新检查目录。
                EnsureDownloadDirectory(directory, createMissing: false);
                await using var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                created = true;
                ObjectStorageProgress.Report(progress, 0, remote.Size);
                byte[] buffer = new byte[64 * 1024];
                while (true)
                {
                    int read = await remote.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    if (maxBytes is { } bodyLimit && transferred > bodyLimit - read)
                        throw ObjectStorageException.FromResource("FilePreviewTooLarge", "PreviewLimitExceeded");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    transferred += read;
                    ObjectStorageProgress.Report(progress, transferred, remote.Size);
                }
                if (remote.Size >= 0 && transferred != remote.Size)
                    throw ObjectStorageException.FromResource("TheDownloadIsIncompleteCheckYourNetworkAndTryAgain", "IncompleteDownload");
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDownloadDirectory(directory, createMissing: false);
            File.Move(temporary, destination, overwrite);
            created = false;
            ObjectStorageProgress.Report(progress, transferred, totalBytes);
        }
        finally
        {
            if (created)
            {
                try
                {
                    EnsureDownloadDirectory(directory, createMissing: false);
                    File.Delete(temporary);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ObjectStorageException) { }
            }
        }
    }

    /// <summary>映射后的子目录由后台创建，拒绝沿目录联接或符号链接写出所选位置。</summary>
    private static void EnsureDownloadDirectory(string path, bool createMissing)
    {
        string fullPath = Path.GetFullPath(path);
        string current = Path.GetPathRoot(fullPath)!;
        foreach (string segment in fullPath[current.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current))
            {
                if (!createMissing)
                    throw ObjectStorageException.FromResource("TheDownloadDirectoryHasChangedSelectTheDestinationAgain", "InvalidLocalPath");
                Directory.CreateDirectory(current);
            }
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw ObjectStorageException.FromResource("TheDownloadDirectoryContainsAJunctionOrSymbolicLinkSelectARegularDirectory", "InvalidLocalPath");
        }
    }
}
