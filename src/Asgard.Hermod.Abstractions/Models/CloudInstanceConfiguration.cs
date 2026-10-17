namespace Asgard.Hermod.Abstractions.Models;

/// <summary>云端逐条实例；普通参数可读，敏感及未知参数只保存密文。</summary>
public sealed record CloudInstanceConfiguration(string Id, string Version, string Name, string TypeId, string ProviderId,
    IReadOnlyDictionary<string, string> Parameters, EncryptedWorkspace Secrets, DateTime CreatedAt, IReadOnlyList<string> TagIds)
{
    /// <summary>诊断输出不包含参数。</summary>
    public override string ToString() => nameof(CloudInstanceConfiguration);
}
