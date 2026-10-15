using System.Net;
using System.Text;
using Tmds.Ssh;

namespace Asgard.Hermod.Ssh;

/// <summary>通过同一条已认证连接创建附加通道，连接释放时所有通道随之终止。</summary>
internal sealed partial class SshConnection
{
    /// <inheritdoc />
    public async Task<SshCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        using var process = await client.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        process.WriteEof();
        byte[] buffer = new byte[8192];
        using var output = new MemoryStream();
        while (true)
        {
            var read = await process.ReadAsync(buffer, buffer, cancellationToken).ConfigureAwait(false);
            if (read.BytesRead == 0) break;
            int remaining = 65536 - (int)output.Length;
            output.Write(buffer, 0, Math.Min(remaining, read.BytesRead));
            if (read.BytesRead > remaining) return new(Encoding.UTF8.GetString(output.ToArray()), -1, true);
        }
        return new(Encoding.UTF8.GetString(output.ToArray()), await process.GetExitCodeAsync(cancellationToken).ConfigureAwait(false), false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SshFileEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        using var sftp = await client.OpenSftpClientAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<SshFileEntry>();
        await foreach (var entry in sftp.GetDirectoryEntriesAsync(path).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (entries.Count >= 10000) throw Text.Exception("SshFilesTooMany");
            string name = entry.Path[(entry.Path.LastIndexOf('/') + 1)..];
            if (name is "." or "..") continue;
            entries.Add(new(name, entry.Path, entry.Attributes.FileType == UnixFileType.Directory,
                entry.Attributes.Length, Convert.ToString((int)entry.Attributes.Permissions, 8), entry.Attributes.LastWriteTime));
        }
        return entries.OrderByDescending(item => item.IsDirectory).ThenBy(item => item.Name, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public async Task UploadAsync(Stream source, string path, IProgress<long>? progress, CancellationToken cancellationToken = default)
    {
        using var sftp = await client.OpenSftpClientAsync(cancellationToken).ConfigureAwait(false);
        string temporary = path + ".hermod-" + Guid.NewGuid().ToString("N") + ".part";
        bool published = false;
        try
        {
            await sftp.UploadFileAsync(source, temporary, overwrite: false, SftpClient.DefaultCreateFilePermissions,
                new SshTransferProgress(progress), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await sftp.RenameAsync(temporary, path, cancellationToken).ConfigureAwait(false); published = true;
        }
        finally
        {
            if (!published)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await sftp.DeleteFileAsync(temporary, cleanup.Token).ConfigureAwait(false); }
                catch (Exception) { /* 断线清理失败不覆盖原始传输错误，目标文件仍未发布。 */ }
            }
        }
    }

    /// <inheritdoc />
    public async Task DownloadAsync(string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken = default)
    {
        using var sftp = await client.OpenSftpClientAsync(cancellationToken).ConfigureAwait(false);
        await sftp.DownloadFileAsync(path, destination, new SshTransferProgress(progress), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        using var sftp = await client.OpenSftpClientAsync(cancellationToken).ConfigureAwait(false);
        await sftp.CreateNewDirectoryAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RenameAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        using var sftp = await client.OpenSftpClientAsync(cancellationToken).ConfigureAwait(false);
        await sftp.RenameAsync(source, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string path, bool directory, CancellationToken cancellationToken = default)
    {
        using var sftp = await client.OpenSftpClientAsync(cancellationToken).ConfigureAwait(false);
        if (directory) await sftp.DeleteDirectoryAsync(path, cancellationToken).ConfigureAwait(false);
        else await sftp.DeleteFileAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IDisposable> ForwardAsync(int localPort, string host, int port, CancellationToken cancellationToken = default)
    {
        if (localPort is < 1 or > 65535 || port is < 1 or > 65535 || string.IsNullOrWhiteSpace(host) || host.Any(char.IsControl))
            throw Text.Exception("SshForwardInvalid");
        return await client.StartForwardAsync(new IPEndPoint(IPAddress.Loopback, localPort), new RemoteHostEndPoint(host, port), cancellationToken).ConfigureAwait(false);
    }
}
