using System.Text.Json.Serialization;

namespace Asgard.Hermod.Agent;

/// <summary>单次请求使用的不可变配置快照；密钥只供传输认证使用。</summary>
public sealed class AgentModelOptions(string endpoint = "", string model = "", string apiKey = "")
{
    /// <summary>兼容 Chat Completions 的基础地址或完整请求地址。</summary>
    public string Endpoint { get; } = endpoint.Trim();
    /// <summary>由用户指定的模型协议标识，不进行本地化。</summary>
    public string Model { get; } = model.Trim();
    /// <summary>仅保留在内存，禁止写入设置、诊断或同步配置。</summary>
    [JsonIgnore]
    public string ApiKey { get; } = apiKey.Trim();
    /// <summary>本地兼容服务可不需要密钥，因此仅判断地址和模型是否合法。</summary>
    public bool IsConfigured => TryEndpoint(out _) && ValidModel && ValidKey;
    private bool ValidModel => Model.Length is > 0 and <= 200 && !Model.Any(char.IsControl);
    private bool ValidKey => ApiKey.Length <= 8192 && !ApiKey.Any(character => char.IsControl(character) || character > 127);

    /// <summary>保存设置时校验配置；空白地址及模型可表示尚未配置。</summary>
    public void Validate(bool allowEmpty = false)
    {
        if (allowEmpty && Endpoint.Length == 0 && Model.Length == 0 && ApiKey.Length == 0) return;
        if (!TryEndpoint(out _)) throw Text.Exception("AgentInvalidEndpoint");
        if (!ValidModel) throw Text.Exception("AgentInvalidModel");
        if (!ValidKey) throw Text.Exception("AgentInvalidKey");
    }

    /// <summary>请求前获取已校验的地址，不在异常中输出原始地址。</summary>
    internal Uri RequestEndpoint()
    {
        if (!IsConfigured) throw Text.Exception("AgentConfigurationRequired");
        TryEndpoint(out var endpoint);
        return endpoint!;
    }

    private bool TryEndpoint(out Uri? result)
    {
        result = null;
        if (Endpoint.Length > 2048 || !Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            (uri.Scheme != Uri.UriSchemeHttps && (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback))) return false;
        string path = uri.AbsolutePath.TrimEnd('/');
        // 不推断特定供应商的版本，只在基础地址后追加标准资源路径。
        result = path.EndsWith("/chat/completions", StringComparison.Ordinal)
            ? new Uri(uri.GetLeftPart(UriPartial.Authority) + path)
            : new Uri(uri.GetLeftPart(UriPartial.Authority) + path + "/chat/completions");
        return true;
    }

    /// <summary>避免调试器或字符串插值意外暴露 API Key 或地址内容。</summary>
    public override string ToString() => nameof(AgentModelOptions);
}
