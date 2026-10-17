using System.Security.Cryptography;
using System.Text;

namespace Asgard.Hermod.Identity;

/// <summary>一次操作派生一个密钥；每条凭据使用独立 nonce 并绑定账户、实例及服务商。</summary>
internal sealed partial class ConfigurationSecrets : IDisposable
{
    private readonly byte[] _key;
    private readonly string _owner;
    private readonly string _salt;
    /// <summary>先验证密码校验包，错误密码不能重新加密覆盖云端。</summary>
    public ConfigurationSecrets(string password, string owner, EncryptedWorkspace protection)
    {
        var check = WorkspaceEncryption.Decrypt(protection, password, owner + ":configuration");
        if (check.SchemaVersion != 1 || check.Instances.Count != 0 || check.Tags.Count != 0 || check.AgentModels.Profiles.Count != 0 || check.AgentModels.DefaultProfileId.Length != 0)
            throw Text.Exception("SyncSnapshotInvalid");
        _owner = owner; _salt = protection.Salt;
        _key = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(_salt), WorkspaceEncryption.Iterations,
            HashAlgorithmName.SHA256, 32);
    }

    /// <summary>序列化敏感参数后立即清除明文字节。</summary>
    public EncryptedWorkspace Encrypt(SyncedInstance instance, IReadOnlyDictionary<string, string> parameters)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(parameters);
        try
        {
            if (plaintext.Length > WorkspaceEncryption.MaximumBytes) throw Text.Exception("SyncSnapshotTooLarge");
            byte[] nonce = RandomNumberGenerator.GetBytes(12), ciphertext = new byte[plaintext.Length], tag = new byte[16];
            using var aes = new AesGcm(_key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Binding(instance.Id, instance.TypeId, instance.ProviderId));
            return new(1, WorkspaceEncryption.Iterations, _salt, Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    /// <summary>服务器元数据不能把密文移交给另一个实例或连接器。</summary>
    public IReadOnlyDictionary<string, string> Decrypt(CloudInstanceConfiguration instance)
    {
        WorkspaceEncryption.ValidateEnvelope(instance.Secrets);
        if (instance.Secrets.Salt != _salt) throw Text.Exception("SyncDecryptionFailed");
        byte[] plaintext = new byte[Convert.FromBase64String(instance.Secrets.Ciphertext).Length];
        try
        {
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(Convert.FromBase64String(instance.Secrets.Nonce), Convert.FromBase64String(instance.Secrets.Ciphertext),
                Convert.FromBase64String(instance.Secrets.Tag), plaintext, Binding(instance.Id, instance.TypeId, instance.ProviderId));
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext) ?? throw Text.Exception("SyncSnapshotInvalid");
        }
        catch (Exception error) when (error is CryptographicException or FormatException or JsonException)
        { throw Text.Exception("SyncDecryptionFailed"); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    /// <summary>附加认证数据使用结构化稳定标识，避免字符串分隔歧义。</summary>
    private byte[] Binding(string id, string type, string provider) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { _owner, id, type, provider }));
    /// <inheritdoc />
    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
