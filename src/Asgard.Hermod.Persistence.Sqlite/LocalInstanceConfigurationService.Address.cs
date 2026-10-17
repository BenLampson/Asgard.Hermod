using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace Asgard.Hermod.Persistence.Sqlite;

public sealed partial class LocalInstanceConfigurationService
{
    private readonly ConcurrentDictionary<string, (byte[] Ciphertext, string Address)> _addresses = new();

    /// <summary>只在密文变更时投影安全地址；标签与名称编辑不重新读取解密连接。</summary>
    private string ReadSafeAddress(StoredInstanceConfiguration instance)
    {
        if (_addresses.TryGetValue(instance.Id, out var cached) && cached.Ciphertext.AsSpan().SequenceEqual(instance.ProtectedParameters))
            return cached.Address;
        byte[]? plaintext = null;
        string address = "";
        try
        {
            plaintext = _protector.Unprotect(instance.ProtectedParameters);
            var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext);
            if (parameters is not null) address = InstanceAddress.Format(instance.TypeId, parameters, instance.ProviderId);
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException)
        { /* 配置损坏仍保留实例行，不展示原始异常或密文。 */ }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
        _addresses[instance.Id] = (instance.ProtectedParameters, address);
        return address;
    }
}
