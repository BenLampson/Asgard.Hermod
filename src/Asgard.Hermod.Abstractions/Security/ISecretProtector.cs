namespace Asgard.Hermod.Abstractions.Security;

/// <summary>隔离设备凭据保护方案，使持久化层始终只接触密文。</summary>
public interface ISecretProtector
{
    /// <summary>保护即将保存的连接参数，不输出原始内容。</summary>
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    /// <summary>在应用服务需要建立连接时解密，不能用于导航展示。</summary>
    byte[] Unprotect(ReadOnlySpan<byte> ciphertext);
}
