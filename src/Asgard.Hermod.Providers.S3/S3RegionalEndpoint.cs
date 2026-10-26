namespace Asgard.Hermod.Providers.S3;

/// <summary>从厂商允许的服务域名提取签名区域，转换协议前缀并保留网络与端口设置。</summary>
public static class S3RegionalEndpoint
{
    /// <summary>仅允许配置的厂商后缀和区域端点，不接受桶域名或其他服务地址。</summary>
    public static (string Region, string Endpoint) Resolve(string value, string[] prefixes, string[] suffixes,
        string targetPrefix, string? bucketRegion, string? globalEndpoint = null, string defaultRegion = "us-east-1")
    {
        var endpoint = ObjectStorageEndpointInput.ParseRoot(value);
        string host = endpoint.Host;
        string? suffix = suffixes.FirstOrDefault(item => host.EndsWith(item, StringComparison.Ordinal));
        string? prefix = prefixes.FirstOrDefault(item => host.StartsWith(item, StringComparison.Ordinal));
        bool global = host == globalEndpoint;
        if (suffix is null || (!global && (prefix is null || host.Length <= prefix.Length + suffix.Length)))
            throw ObjectStorageException.FromResource("StorageCloudEndpointInvalid", "InvalidEndpoint");
        string region = global ? defaultRegion : host[prefix!.Length..^suffix.Length];
        try { S3ConnectionSettings.ValidateRegion(region); }
        catch (ObjectStorageException) { throw ObjectStorageException.FromResource("StorageCloudEndpointInvalid", "InvalidEndpoint"); }
        if (!string.IsNullOrWhiteSpace(bucketRegion))
        {
            S3ConnectionSettings.ValidateRegion(bucketRegion);
            region = bucketRegion;
        }
        var target = new UriBuilder(endpoint);
        if (!global || !string.IsNullOrWhiteSpace(bucketRegion)) target.Host = $"{targetPrefix}{region}{suffix}";
        return (region, target.Uri.AbsoluteUri);
    }
}
