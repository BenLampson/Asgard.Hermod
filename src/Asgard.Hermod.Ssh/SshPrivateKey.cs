using System.Security.Cryptography;
using Tmds.Ssh;

namespace Asgard.Hermod.Ssh;

/// <summary>导入有界私钥，并从内存建立凭据，不产生临时明文文件。</summary>
internal static class SshPrivateKey
{
    /// <summary>已有导入内容幂等保留；旧路径在当前设备保存时转换。</summary>
    internal static async Task<string> ImportAsync(string value, CancellationToken token)
    {
        if (InstanceFileContent.IsImported(value))
        {
            char[] chars = InstanceFileContent.Decode(value); Array.Clear(chars); return value;
        }
        byte[]? bytes = null;
        try
        {
            await using var stream = new FileStream(value, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (stream.Length is 0 or > InstanceFileContent.MaximumBytes) throw Text.Exception("SshKeyImportFailed");
            bytes = new byte[stream.Length]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            // 若读取期间文件变大，不能只保存前缀并宣称导入成功。
            if (stream.ReadByte() != -1) throw Text.Exception("SshKeyImportFailed");
            return InstanceFileContent.Encode(bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw Text.Exception("SshKeyImportFailed"); }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }
    /// <summary>旧路径继续兼容；可移植配置在协议解析完成后清空临时字符数组。</summary>
    internal static Credential Credential(SshHostOptions host)
    {
        if (!InstanceFileContent.IsImported(host.KeyPath))
        {
            if (!File.Exists(host.KeyPath)) throw new SshException("SshKeyUnavailable", "SSH_KEY_UNAVAILABLE");
            return new PrivateKeyCredential(host.KeyPath, host.Passphrase);
        }
        return new SshImportedKeyCredential(host);
    }
}
