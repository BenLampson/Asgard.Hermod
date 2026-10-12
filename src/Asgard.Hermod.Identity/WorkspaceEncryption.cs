using System.Security.Cryptography;
using System.Text;

namespace Asgard.Hermod.Identity;

/// <summary>端到端加密绑定账号和服务器；同步密码只在本次操作内存中使用。</summary>
public static class WorkspaceEncryption
{
    /// <summary>格式一固定派生成本，拒绝服务端提供任意工作因子造成资源耗尽。</summary>
    public const int Iterations = 600000;
    /// <summary>限制明文及密文为四 MiB，避免不受限的快照解析。</summary>
    public const int MaximumBytes = 4 * 1024 * 1024;

    /// <summary>每次上传生成独立 salt 和 nonce，不复用 GCM nonce。</summary>
    public static EncryptedWorkspace Encrypt(WorkspaceSnapshot snapshot, string password, string owner)
    {
        ValidatePassword(password);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            if (plaintext.Length > MaximumBytes) throw Text.Exception("SyncSnapshotTooLarge");
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(owner));
            return new(1, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); CryptographicOperations.ZeroMemory(key); }
    }

    /// <summary>密码错误、篡改或账号不一致统一报安全提示，本地数据库尚未更改。</summary>
    public static WorkspaceSnapshot Decrypt(EncryptedWorkspace envelope, string password, string owner)
    {
        ValidatePassword(password);
        try
        {
            ValidateEnvelope(envelope);
            byte[] salt = Convert.FromBase64String(envelope.Salt);
            byte[] ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            byte[] plaintext = new byte[ciphertext.Length];
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(Convert.FromBase64String(envelope.Nonce), ciphertext, Convert.FromBase64String(envelope.Tag),
                    plaintext, Encoding.UTF8.GetBytes(owner));
                return JsonSerializer.Deserialize<WorkspaceSnapshot>(plaintext) ?? throw Text.Exception("SyncSnapshotInvalid");
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); CryptographicOperations.ZeroMemory(key); }
        }
        catch (Exception error) when (error is CryptographicException or FormatException or JsonException)
        { throw Text.Exception("SyncDecryptionFailed"); }
    }

    /// <summary>同步密码不保存，限制输入长度以保持跨设备一致。</summary>
    public static void ValidatePassword(string password)
    {
        if (password is null || password.Length is < 12 or > 128) throw Text.Exception("SyncPasswordInvalid");
    }

    /// <summary>服务器与客户端共用的密文结构约束，不解密任何业务内容。</summary>
    public static void ValidateEnvelope(EncryptedWorkspace envelope)
    {
        if (envelope is null || envelope.FormatVersion != 1 || envelope.Iterations != Iterations
            || envelope.Salt is null || envelope.Nonce is null || envelope.Tag is null || envelope.Ciphertext is null
            || envelope.Salt.Length > 32 || envelope.Nonce.Length > 24 || envelope.Tag.Length > 32
            || envelope.Ciphertext.Length > (MaximumBytes + 2) / 3 * 4)
            throw Text.Exception("SyncSnapshotInvalid");
        try
        {
            if (Convert.FromBase64String(envelope.Salt).Length != 16 || Convert.FromBase64String(envelope.Nonce).Length != 12
                || Convert.FromBase64String(envelope.Tag).Length != 16 || Convert.FromBase64String(envelope.Ciphertext).Length is 0 or > MaximumBytes)
                throw Text.Exception("SyncSnapshotInvalid");
        }
        catch (FormatException) { throw Text.Exception("SyncSnapshotInvalid"); }
    }
}
