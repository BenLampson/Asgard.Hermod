using System.Text;

namespace Asgard.Hermod.Abstractions.Models;

/// <summary>敏感文件在连接参数中的可移植编码；实际保护由本机和云端加密层负责。</summary>
public static class InstanceFileContent
{
    private const string Prefix = "hermod-secret-file:v1:";
    /// <summary>限制导入大小，避免不受控文件进入配置或同步请求。</summary>
    public const int MaximumBytes = 32768;
    /// <summary>辨认已导入内容，界面不能把其编码当作文件路径显示。</summary>
    public static bool IsImported(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);
    /// <summary>保留文件原始字节，编码不包含换行等参数禁止的控制字符。</summary>
    public static string Encode(ReadOnlySpan<byte> contents)
    {
        if (contents.Length is 0 or > MaximumBytes) throw Text.Exception("SshKeyImportFailed");
        return Prefix + Convert.ToBase64String(contents);
    }
    /// <summary>只解码已声明的导入格式，错误反馈不包含文件内容。</summary>
    public static char[] Decode(string value)
    {
        if (!IsImported(value) || value.Length > 65536) throw Text.Exception("SshKeyImportFailed");
        byte[]? bytes = null;
        try
        {
            bytes = Convert.FromBase64String(value[Prefix.Length..]);
            if (bytes.Length is 0 or > MaximumBytes) throw Text.Exception("SshKeyImportFailed");
            int offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            return new UTF8Encoding(false, true).GetChars(bytes, offset, bytes.Length - offset);
        }
        catch (Exception error) when (error is FormatException or DecoderFallbackException) { throw Text.Exception("SshKeyImportFailed"); }
        finally { if (bytes is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
}
