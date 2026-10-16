using System.Security.Cryptography;
using System.Text;

namespace Asgard.Hermod.Identity;

/// <summary>模型配置使用独立用途的账号密文，不修改不可替换的同步密码校验包。</summary>
internal sealed partial class ConfigurationSecrets
{
    /// <summary>名称、地址、模型和密钥整体加密，账号和用途参与认证。</summary>
    public EncryptedWorkspace EncryptAgentModels(AgentModelSet models)
    {
        AgentModelValidation.Validate(models); byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(models);
        try
        {
            if (plaintext.Length > 1024 * 1024) throw Text.Exception("SyncSnapshotTooLarge");
            byte[] nonce = RandomNumberGenerator.GetBytes(12), ciphertext = new byte[plaintext.Length], tag = new byte[16];
            using var aes = new AesGcm(_key, 16); aes.Encrypt(nonce, plaintext, ciphertext, tag, ModelBinding());
            return new(1, WorkspaceEncryption.Iterations, _salt, Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    /// <summary>旧云端未保存模型时为空；密文不能移交给其他账号或其他用途。</summary>
    public AgentModelSet DecryptAgentModels(EncryptedWorkspace? envelope)
    {
        if (envelope is null) return AgentModelSet.Empty;
        WorkspaceEncryption.ValidateEnvelope(envelope);
        if (envelope.Salt != _salt) throw Text.Exception("SyncDecryptionFailed");
        byte[] plaintext = new byte[Convert.FromBase64String(envelope.Ciphertext).Length];
        try
        {
            if (plaintext.Length > 1024 * 1024) throw Text.Exception("SyncSnapshotTooLarge");
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(Convert.FromBase64String(envelope.Nonce), Convert.FromBase64String(envelope.Ciphertext), Convert.FromBase64String(envelope.Tag), plaintext, ModelBinding());
            var models = JsonSerializer.Deserialize<AgentModelSet>(plaintext) ?? throw Text.Exception("SyncSnapshotInvalid");
            AgentModelValidation.Validate(models); return models.Freeze();
        }
        catch (Exception error) when (error is CryptographicException or FormatException or JsonException) { throw Text.Exception("SyncDecryptionFailed"); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    private byte[] ModelBinding() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { _owner, "hermod.agent.models.v1" }));
}
