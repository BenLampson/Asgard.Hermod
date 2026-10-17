namespace Asgard.Hermod.Identity;

/// <summary>桌面 public Client 配置，不包含服务端密钥。</summary>
public sealed class HermodIdentityOptions
{
    /// <summary>Heimdall 精确发行者。</summary>
    public string Authority { get; set; } = "";
    /// <summary>桌面客户端标识。</summary>
    public string ClientId { get; set; } = "";
    /// <summary>唯一租户标识。</summary>
    public string TenantId { get; set; } = "";
    /// <summary>Hermod 应用标识。</summary>
    public string ApplicationId { get; set; } = "";
    /// <summary>同步资源受众。</summary>
    public string Audience { get; set; } = "hermod-api";
    /// <summary>登记的登录范围。</summary>
    public string Scope { get; set; } = "openid profile offline_access hermod.sync";
    /// <summary>只在本机接收浏览器回调。</summary>
    public string RedirectUri { get; set; } = "http://127.0.0.1:43821/callback/";
    /// <summary>桌面调用的资源 API 根地址。</summary>
    public string ApiBaseUrl { get; set; } = "";
    /// <summary>可选固定服务器地址；连接仍使用 ApiBaseUrl 的 Host、SNI 与完整证书校验。</summary>
    public string ApiServerAddress { get; set; } = "";

    /// <summary>回调只能绑定 IPv4 回环地址；生产 API 与发行者必须使用 HTTPS。</summary>
    public void Validate()
    {
        if (!HeimdallClientOptions.IsTrustedUrl(Authority, false) || !HeimdallClientOptions.IsTrustedUrl(ApiBaseUrl, true)
            || !Guid.TryParse(TenantId, out _) || !Guid.TryParse(ApplicationId, out _)
            || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(Audience)
            || !Scope.Split(' ').Contains("openid") || !Scope.Split(' ').Contains("profile")
            || !Uri.TryCreate(RedirectUri, UriKind.Absolute, out var callback)
            || callback.Scheme != "http" || callback.Host != "127.0.0.1" || callback.IsDefaultPort
            || callback.UserInfo.Length != 0 || callback.Query.Length != 0 || callback.Fragment.Length != 0
            || !callback.AbsolutePath.EndsWith('/')
            || (!string.IsNullOrEmpty(ApiServerAddress) && !System.Net.IPAddress.TryParse(ApiServerAddress, out _)))
            throw Text.Exception("IdentityConfigurationInvalid");
    }

    /// <summary>复用官方协议层的 PKCE、nonce 和签名校验。</summary>
    public HeimdallOAuthOptions ToOAuthOptions() => new()
    {
        Authority = Authority,
        ClientId = ClientId,
        TenantId = TenantId,
        ApplicationId = ApplicationId,
        Audience = Audience,
        Scope = Scope,
        RedirectUri = RedirectUri,
        AllowLocalhostHttp = true,
    };
}
