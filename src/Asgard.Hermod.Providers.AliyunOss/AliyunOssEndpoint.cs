namespace Asgard.Hermod.Providers.AliyunOss;

/// <summary>限制 OSS 区域、服务端点与容器的地址组成字符，凭据仅发送到官方服务域名。</summary>
public static class AliyunOssEndpoint
{
    /// <summary>区域方式自动生成公网端点；地址方式提取签名区域并保留内网或双栈线路。</summary>
    public static (string Region, Uri Endpoint) Resolve(InstanceConnectionOptions options, string? bucketRegion)
    {
        var parameters = ObjectStorageEndpointInput.Normalize(options).Parameters;
        if (parameters["endpointMode"] == "region")
        {
            string value = parameters.GetValueOrDefault("region", "").Trim();
            string configuredRegion = value.Length == 0 ? "cn-hangzhou" : value;
            string region = NormalizeRegion(string.IsNullOrWhiteSpace(bucketRegion) ? configuredRegion : bucketRegion);
            return (region, new($"https://oss-{region}.aliyuncs.com"));
        }
        var endpoint = ObjectStorageEndpointInput.ParseRoot(parameters.GetValueOrDefault("endpoint", ""));
        string host = endpoint.Host;
        bool dualStack = host.EndsWith(".oss.aliyuncs.com", StringComparison.Ordinal);
        string valueRegion;
        if (dualStack) valueRegion = host[..^".oss.aliyuncs.com".Length];
        else if (host.StartsWith("oss-", StringComparison.Ordinal) && host.EndsWith(".aliyuncs.com", StringComparison.Ordinal))
            valueRegion = host[4..^".aliyuncs.com".Length];
        else throw ObjectStorageException.FromResource("StorageCloudEndpointInvalid", "InvalidEndpoint");
        bool internalNetwork = !dualStack && valueRegion.EndsWith("-internal", StringComparison.Ordinal);
        if (internalNetwork) valueRegion = valueRegion[..^"-internal".Length];
        string selectedRegion;
        try { selectedRegion = NormalizeRegion(valueRegion); }
        catch (ObjectStorageException) { throw ObjectStorageException.FromResource("StorageCloudEndpointInvalid", "InvalidEndpoint"); }
        if (!string.IsNullOrWhiteSpace(bucketRegion)) selectedRegion = NormalizeRegion(bucketRegion);
        var target = new UriBuilder(endpoint)
        {
            Host = dualStack ? $"{selectedRegion}.oss.aliyuncs.com"
                : $"oss-{selectedRegion}{(internalNetwork ? "-internal" : "")}.aliyuncs.com",
        };
        return (selectedRegion, target.Uri);
    }

    /// <summary>OSS 旧 Location 含 oss- 前缀；新 Region 可直接使用。</summary>
    public static string NormalizeRegion(string? region)
    {
        string value = region?.StartsWith("oss-", StringComparison.Ordinal) == true ? region[4..] : region ?? "";
        if (value.Length is < 2 or > 48 || value[0] is < 'a' or > 'z' || value.EndsWith('-') ||
            value.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-'))
            throw ObjectStorageException.FromResource("InvalidBucketRegionRefreshTheBucketList", "InvalidRegion");
        return value;
    }

    /// <summary>普通 OSS 容器名称只允许小写 ASCII、数字和连字符。</summary>
    public static void ValidateBucket(string bucket)
    {
        if (bucket is null || bucket.Length is < 3 or > 63 || bucket.StartsWith('-') || bucket.EndsWith('-') ||
            bucket.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-'))
            throw ObjectStorageException.FromResource("InvalidBucketNameSelectTheBucketAgain", "InvalidBucket");
    }
}
