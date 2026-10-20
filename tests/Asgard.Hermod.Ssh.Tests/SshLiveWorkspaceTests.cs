using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>仅针对明确配置的本机隔离 OpenSSH fixture 验证新增通道。</summary>
public sealed class SshLiveWorkspaceTests
{
    /// <summary>沿用既有专用测试服务开关，不读取用户 SSH 配置。</summary>
    public static bool IsConfigured => SshLiveConnectionTests.IsConfigured;

    /// <summary>SFTP、命令、监测与端口转发在 PTY 存活期间复用同一条核验后的连接。</summary>
    [Fact(Skip = "Configure the isolated OpenSSH fixture to enable network tests.", SkipUnless = nameof(IsConfigured))]
    public async Task AuxiliaryChannelsShareVerifiedTransport()
    {
        var trust = new LiveHostTrust(); var factory = new SshConnectionFactory(trust, trust);
        using var timeout = SshLiveConnectionTests.Timeout();
        await using var opened = await factory.OpenAsync(SshLiveConnectionTests.Options(), 90, 30, timeout.Token);
        var connection = Assert.IsAssignableFrom<ISshWorkspaceConnection>(opened);
        string directory = "/tmp/hermod-workspace-" + Guid.NewGuid().ToString("N"), file = directory + "/file '中文'.txt", renamed = directory + "/renamed.txt";
        await connection.CreateDirectoryAsync(directory, timeout.Token);
        try
        {
            byte[] content = Encoding.UTF8.GetBytes("SFTP 中文\n");
            await connection.UploadAsync(new MemoryStream(content), file, null, timeout.Token);
            Assert.Contains(await connection.ListDirectoryAsync(directory, timeout.Token), entry => entry.Path == file && entry.Length == content.Length);
            using var downloaded = new MemoryStream(); await connection.DownloadAsync(file, downloaded, null, timeout.Token); Assert.Equal(content, downloaded.ToArray());
            await Assert.ThrowsAnyAsync<Exception>(() => connection.UploadAsync(new MemoryStream("replacement"u8.ToArray()), file, null, timeout.Token));
            using var original = new MemoryStream(); await connection.DownloadAsync(file, original, null, timeout.Token); Assert.Equal(content, original.ToArray());
            await connection.RenameAsync(file, renamed, timeout.Token);
            var result = await connection.ExecuteCommandAsync("printf 'independent command'; exit 7", timeout.Token);
            Assert.Equal(7, result.ExitCode); Assert.Contains("independent command", result.Output);
            var bounded = await connection.ExecuteCommandAsync("yes hermod-test-output | head -c 70000", timeout.Token);
            Assert.True(bounded.Truncated); Assert.InRange(bounded.Output.Length, 1, 65536);
            var snapshot = await new SshHostMonitor().ReadAsync(connection, timeout.Token); Assert.True(snapshot.MemoryTotal > 0);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            using var forward = await connection.ForwardAsync(port, "127.0.0.1", 22, timeout.Token);
            using var tcp = new TcpClient(); await tcp.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            var banner = new byte[256]; int read = await tcp.GetStream().ReadAsync(banner, timeout.Token); Assert.StartsWith("SSH-", Encoding.ASCII.GetString(banner, 0, read));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            foreach (string path in new[] { file, renamed }) { try { await connection.DeleteAsync(path, false, cleanup.Token); } catch (Exception) { } }
            await connection.DeleteAsync(directory, true, cleanup.Token);
        }
        var disconnected = connection.Disconnected;
        await connection.DisposeAsync();
        Assert.Equal(disconnected, connection.Disconnected); Assert.True(disconnected.IsCancellationRequested);
        await connection.DisposeAsync();
    }
}
