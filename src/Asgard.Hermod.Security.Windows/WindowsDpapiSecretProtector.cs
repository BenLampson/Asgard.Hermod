using System.Security.Cryptography;

namespace Asgard.Hermod.Security.Windows;

/// <summary>使用 Windows 当前用户 DPAPI 保护参数，数据库复制到其他用户后不能解密。</summary>
public sealed class WindowsDpapiSecretProtector : ISecretProtector
{
    /// <summary>应用用途隔离熵；不是密钥，不依赖随数据库保存的明文密码。</summary>
    private static readonly byte[] Entropy = "Asgard.Hermod.LocalConnection.v1"u8.ToArray();

    /// <inheritdoc />
    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        byte[] buffer = plaintext.ToArray();
        try
        {
            return ProtectedData.Protect(buffer, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // 不把操作系统原始异常附加到面向界面的异常链。
            throw new InvalidOperationException("无法保护连接凭据，请检查当前 Windows 用户的凭据保护状态。");
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    /// <inheritdoc />
    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext)
    {
        byte[] buffer = ciphertext.ToArray();
        try
        {
            return ProtectedData.Unprotect(buffer, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("无法读取此连接凭据，请使用原 Windows 用户或重新配置实例。");
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
}
