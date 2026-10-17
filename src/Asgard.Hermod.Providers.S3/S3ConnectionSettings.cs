namespace Asgard.Hermod.Providers.S3;

/// <summary>规范化连接参数；不将凭据写入端点、日志或默认对象字符串。</summary>
internal sealed class S3ConnectionSettings
{
    internal required string AccessKey { get; init; }
    internal required string SecretKey { get; init; }
    internal string? SessionToken { get; init; }
    internal required string Region { get; init; }
    internal required Uri Endpoint { get; init; }
    internal string? Bucket { get; init; }
    internal bool PathStyle { get; init; }

    /// <summary>云端地址只从服务商规则生成；自建服务允许显式填写 HTTP 或 HTTPS 服务根地址。</summary>
    internal static S3ConnectionSettings Parse(S3ProviderProfile profile, InstanceConnectionOptions options, string? region)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TypeId != "storage" || options.ProviderId != profile.Id)
            throw ObjectStorageException.FromResource("TheConnectionConfigurationDoesNotMatchTheSelectedInstanceTypeOrProvider", "ProviderMismatch");
        string Get(string key) => options.Parameters.GetValueOrDefault(key, "").Trim();
        string access = Get("accessKeyId"), secret = Get("accessKeySecret"), session = Get("sessionToken");
        if (access.Length is 0 or > 256 || secret.Length is 0 or > 512 || session.Length > 16384 ||
            access.Any(char.IsControl) || secret.Any(char.IsControl) || session.Any(char.IsControl))
            throw ObjectStorageException.FromResource("InvalidConnectionCredentialsEditTheInstanceConfiguration", "InvalidCredentials");
        string selectedRegion = string.IsNullOrWhiteSpace(region) ? Get("region") : region;
        if (selectedRegion.Length == 0) selectedRegion = profile.DefaultRegion;
        ValidateRegion(selectedRegion);
        string address = profile.CustomEndpoint ? Get("endpoint") : profile.Endpoint(selectedRegion);
        if (!Uri.TryCreate(address, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("https" or "http") ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.AbsolutePath != "/" || endpoint.HostNameType == UriHostNameType.Unknown)
            throw ObjectStorageException.FromResource("StorageEndpointInvalid", "InvalidEndpoint");
        string bucket = Get("bucket");
        if (bucket.Length > 0) ValidateBucket(bucket);
        string style = Get("addressingStyle");
        if (style.Length > 0 && style is not ("path" or "virtual"))
            throw ObjectStorageException.FromResource("StorageAddressingInvalid", "InvalidAddressingStyle");
        return new()
        {
            AccessKey = access,
            SecretKey = secret,
            SessionToken = session.Length == 0 ? null : session,
            Region = selectedRegion,
            Endpoint = endpoint,
            Bucket = bucket.Length == 0 ? null : bucket,
            PathStyle = profile.CustomEndpoint ? style != "virtual" : profile.PathStyle,
        };
    }

    /// <summary>区域进入主机名及签名范围前按稳定协议字符验证。</summary>
    internal static void ValidateRegion(string value)
    {
        if (value.Length is 0 or > 64 || !value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') ||
            !char.IsAsciiLetterLower(value[0]) || value.EndsWith('-'))
            throw ObjectStorageException.FromResource("StorageRegionInvalid", "InvalidRegion");
    }

    /// <summary>禁止桶名成为路径、查询或凭据重定向入口；兼容旧式 S3 桶名。</summary>
    internal static void ValidateBucket(string value)
    {
        if (value.Length is < 3 or > 255 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_') ||
            !char.IsAsciiLetterOrDigit(value[0]) || !char.IsAsciiLetterOrDigit(value[^1]) || value.Contains("..", StringComparison.Ordinal))
            throw ObjectStorageException.FromResource("StorageBucketInvalid", "InvalidBucket");
    }

    /// <inheritdoc />
    public override string ToString() => "S3 connection (credentials hidden)";
}
