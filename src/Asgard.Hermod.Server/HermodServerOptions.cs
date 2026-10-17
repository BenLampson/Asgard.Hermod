namespace Asgard.Hermod.Server;

/// <summary>服务端独立身份配置，密钥只通过环境变量或安全配置提供。</summary>
public sealed class HermodServerOptions
{
    /// <summary>生产精确发行者。</summary>
    public string Authority { get; set; } = "";
    /// <summary>Heimdall API 根地址。</summary>
    public string IdentityApiBaseUrl { get; set; } = "";
    /// <summary>唯一租户。</summary>
    public string TenantId { get; set; } = "";
    /// <summary>唯一应用。</summary>
    public string ApplicationId { get; set; } = "";
    /// <summary>唯一桌面 public Client，拒绝同受众的其他客户端。</summary>
    public string DesktopClientId { get; set; } = "";
    /// <summary>服务端 confidential Client。</summary>
    public string ServiceClientId { get; set; } = "";
    /// <summary>仅在 Token Endpoint 使用，不记录、不下发桌面。</summary>
    public string ServiceClientSecret { get; set; } = "";
    /// <summary>密钥版本变化时官方 SDK 放弃旧缓存。</summary>
    public string ServiceCredentialVersion { get; set; } = "1";
    /// <summary>独立同步数据库文件，容器必须挂载持久卷。</summary>
    public string DatabasePath { get; set; } = "hermod-sync.db";
    /// <summary>启动时拒绝缺失身份边界或秘密。</summary>
    public bool IsValid() => HeimdallClientOptions.IsTrustedUrl(Authority, false)
        && HeimdallClientOptions.IsTrustedUrl(IdentityApiBaseUrl, false)
        && Guid.TryParse(TenantId, out _) && Guid.TryParse(ApplicationId, out _)
        && !string.IsNullOrWhiteSpace(DesktopClientId) && !string.IsNullOrWhiteSpace(ServiceClientId)
        && ServiceClientSecret.Length >= 32 && !string.IsNullOrWhiteSpace(ServiceCredentialVersion)
        && !string.IsNullOrWhiteSpace(DatabasePath) && DatabasePath != ":memory:";
}
