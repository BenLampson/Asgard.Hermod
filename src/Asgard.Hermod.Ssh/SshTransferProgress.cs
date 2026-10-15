using Tmds.Ssh;

namespace Asgard.Hermod.Ssh;

/// <summary>将 SDK 字节进度投射为调用方的进度接口，不携带路径或凭据。</summary>
internal sealed class SshTransferProgress(IProgress<long>? progress) : SftpProgressHandler
{
    /// <inheritdoc />
    protected override void DataTransferred(int entryIndex, long transferred, long totalTransferred) => progress?.Report(totalTransferred);
}
