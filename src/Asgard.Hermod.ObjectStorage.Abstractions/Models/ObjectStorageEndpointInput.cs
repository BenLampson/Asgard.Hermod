namespace Asgard.Hermod.ObjectStorage.Abstractions.Models;

/// <summary>云存储共用的地址输入方式、条件字段与旧配置转换，不包含厂商域名规则。</summary>
public static class ObjectStorageEndpointInput
{
    /// <summary>沿用稳定协议值；切换只改变可见字段，隐藏草稿由向导保留。</summary>
    public static IReadOnlyList<InstanceConfigurationField> CreateFields(string defaultRegion, string endpointHint) =>
        Array.AsReadOnly<InstanceConfigurationField>([
            new("endpointMode", Text.Get("StorageEndpointMode"), InstanceFieldKind.Choice, MaxLength: 8)
            {
                DefaultValue = "region",
                Choices = [new("region", Text.Get("StorageEndpointByRegion")), new("endpoint", Text.Get("StorageEndpointByAddress"))],
            },
            new("region", Text.Get("StorageRegion"), IsRequired: false, MaxLength: 64,
                Hint: Text.Format("StorageRegionHint", defaultRegion)) { VisibleWhenKey = "endpointMode", VisibleWhenValue = "region" },
            new("endpoint", Text.Get("StorageEndpoint"), MaxLength: 2048, Hint: endpointHint)
                { VisibleWhenKey = "endpointMode", VisibleWhenValue = "endpoint" },
        ]);

    /// <summary>缺少选择值的旧配置默认为区域模式；兼容早期将完整地址保存在 region 的格式。</summary>
    public static InstanceConnectionOptions Normalize(InstanceConnectionOptions options)
    {
        var parameters = new Dictionary<string, string>(options.Parameters, StringComparer.Ordinal);
        string mode = parameters.GetValueOrDefault("endpointMode", "").Trim();
        if (mode.Length == 0)
        {
            string region = parameters.GetValueOrDefault("region", "").Trim();
            mode = region.Contains('.') || region.Contains("://", StringComparison.Ordinal)
                ? "endpoint" : "region";
            if (mode == "endpoint") parameters["endpoint"] = region;
        }
        parameters["endpointMode"] = mode;
        if (mode == "region") parameters.Remove("endpoint");
        else if (mode == "endpoint") parameters.Remove("region");
        else throw ObjectStorageException.FromResource("StorageEndpointModeInvalid", "InvalidEndpointMode");
        return new() { TypeId = options.TypeId, ProviderId = options.ProviderId, Parameters = parameters };
    }

    /// <summary>服务根地址统一拒绝凭据、桶路径和查询；省略协议的主机名默认采用 HTTPS。</summary>
    public static Uri ParseRoot(string value)
    {
        value = value.Trim();
        string address = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
        if (value.Length is 0 or > 2048 || value.Any(char.IsControl) || value.Contains('\\') ||
            !Uri.TryCreate(address, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("https" or "http") ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.AbsolutePath != "/" || endpoint.HostNameType == UriHostNameType.Unknown)
            throw ObjectStorageException.FromResource("StorageEndpointInvalid", "InvalidEndpoint");
        return endpoint;
    }
}
