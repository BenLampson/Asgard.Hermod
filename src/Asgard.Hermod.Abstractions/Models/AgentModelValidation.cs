using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.Abstractions.Models;

/// <summary>编辑、设备恢复和账号同步共用的模型配置边界。</summary>
public static class AgentModelValidation
{
    /// <summary>验证配置集合，不自动修复目标地址或沿用其他配置的密钥。</summary>
    public static void Validate(AgentModelSet value)
    {
        if (value is null || value.Profiles is null || value.Profiles.Count > 32 || value.DefaultProfileId is null ||
            value.Profiles.Any(profile => profile is null) || value.Profiles.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count() != value.Profiles.Count)
            throw Text.Exception("AgentModelDataInvalid");
        foreach (var profile in value.Profiles)
        {
            if (!Guid.TryParseExact(profile.Id, "N", out _) || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80 || profile.Name != profile.Name.Trim() || profile.Name.Any(char.IsControl) ||
                profile.UpdatedAt.Offset != TimeSpan.Zero) throw Text.Exception("AgentModelDataInvalid");
            ValidateOptions(profile.Endpoint, profile.Model, profile.ApiKey);
        }
        if (value.DefaultProfileId.Length > 0 && value.Profiles.All(profile => profile.Id != value.DefaultProfileId)) throw Text.Exception("AgentModelDataInvalid");
    }
    /// <summary>请求与编辑使用相同规则；仅回环服务允许 HTTP，禁止地址内凭据、查询串和片段。</summary>
    public static void ValidateOptions(string endpoint, string model, string apiKey, bool allowEmpty = false)
    {
        if (allowEmpty && endpoint == "" && model == "" && apiKey == "") return;
        if (!TryRequestEndpoint(endpoint, out _)) throw Text.Exception("AgentInvalidEndpoint");
        if (string.IsNullOrWhiteSpace(model) || model != model.Trim() || model.Length > 200 || model.Any(char.IsControl)) throw Text.Exception("AgentInvalidModel");
        if (apiKey is null || apiKey.Length > 8192 || apiKey.Any(character => char.IsControl(character) || character > 127)) throw Text.Exception("AgentInvalidKey");
    }
    /// <summary>地址只追加标准资源路径，不推断供应商版本。</summary>
    public static bool TryRequestEndpoint(string? endpoint, out Uri? result)
    {
        result = null;
        if (endpoint is null || endpoint.Length > 2048 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            (uri.Scheme != Uri.UriSchemeHttps && (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback))) return false;
        string path = uri.AbsolutePath.TrimEnd('/');
        result = new Uri(uri.GetLeftPart(UriPartial.Authority) + path + (path.EndsWith("/chat/completions", StringComparison.Ordinal) ? "" : "/chat/completions"));
        return true;
    }
}
