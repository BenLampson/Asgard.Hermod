namespace Asgard.Hermod.Abstractions.Models;

/// <summary>设备级主机身份记录，不随账户配置同步。</summary>
public sealed record TrustedHostKey(string Host, int Port, string Algorithm, string Fingerprint);
