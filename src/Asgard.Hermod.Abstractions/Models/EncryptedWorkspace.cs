namespace Asgard.Hermod.Abstractions.Models;

/// <summary>版本一采用 PBKDF2-SHA256 和 AES-256-GCM，服务器只保存此密文。</summary>
public sealed record EncryptedWorkspace(int FormatVersion, int Iterations, string Salt, string Nonce, string Tag, string Ciphertext);
