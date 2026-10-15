namespace Asgard.Hermod.Ssh;

/// <summary>已核验 SSH 传输上的附加通道；文件、命令和转发不会向交互 PTY 注入内容。</summary>
public interface ISshWorkspaceConnection : ISshConnection
{
    /// <summary>读取有界命令输出；取消会关闭该命令通道。</summary>
    Task<SshCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default);
    /// <summary>读取当前目录，最多返回指定数量；不递归跟随符号链接。</summary>
    Task<IReadOnlyList<SshFileEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default);
    /// <summary>上传一个新文件；默认不覆盖远端现有文件。</summary>
    Task UploadAsync(Stream source, string path, IProgress<long>? progress, CancellationToken cancellationToken = default);
    /// <summary>下载到调用者拥有的流。</summary>
    Task DownloadAsync(string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken = default);
    /// <summary>创建单个目录，不能递归创建或覆盖。</summary>
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);
    /// <summary>重命名文件；目标存在时由服务器拒绝。</summary>
    Task RenameAsync(string source, string destination, CancellationToken cancellationToken = default);
    /// <summary>删除单个文件或空目录，禁止递归删除。</summary>
    Task DeleteAsync(string path, bool directory, CancellationToken cancellationToken = default);
    /// <summary>仅在本机回环地址监听，返回的句柄负责停止转发。</summary>
    Task<IDisposable> ForwardAsync(int localPort, string host, int port, CancellationToken cancellationToken = default);
}
